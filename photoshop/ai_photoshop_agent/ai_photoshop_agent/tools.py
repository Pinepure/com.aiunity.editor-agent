from __future__ import annotations

from .models import AiManifestBundleDefinition, AiPhotoshopAgentConfig, AiToolDanger, AiToolDefinition, AiToolExecutionContext, AiToolRegistry, clamp_number, schema_json


def default_bundles() -> list[AiManifestBundleDefinition]:
    return [
        AiManifestBundleDefinition(id="service", description="Adapter health, logs, config, and recent calls.", prefixes=["service."]),
        AiManifestBundleDefinition(id="photoshop.bridge", description="Bridge and plugin heartbeat inspection tools.", prefixes=["photoshop.bridge_"]),
        AiManifestBundleDefinition(id="photoshop.documents", description="Photoshop document and layer inspection or mutation tools.", prefixes=["photoshop.documents_", "photoshop.active_document_", "photoshop.layers_", "photoshop.layer_", "photoshop.document_"]),
        AiManifestBundleDefinition(id="tooling.dynamic", description="Generated tool management and dynamic registration tools.", prefixes=["tool."]),
    ]


def register_default_tools(registry: AiToolRegistry, context: AiToolExecutionContext) -> None:
    register = registry.register

    register(
        AiToolDefinition(
            id="service.health_get",
            description="Return the adapter health payload.",
            args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="service.health_get",
            handler=lambda _args, ctx: ctx.build_health_payload(include_ok=True),
        )
    )
    register(
        AiToolDefinition(
            id="service.agent_brief_get",
            description="Return the concise operating brief for this adapter.",
            args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="service.agent_brief_get",
            handler=lambda _args, ctx: ctx.build_agent_brief_payload(include_ok=True),
        )
    )
    register(
        AiToolDefinition(
            id="service.config_get",
            description="Return effective adapter configuration.",
            args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="service.config_get",
            handler=lambda _args, ctx: {
                "stateDirectoryPath": str(ctx.config.state_directory_path),
                "bridgeDir": str(ctx.config.bridge_dir),
                "generatedToolsDirectoryPath": str(ctx.config.generated_tools_directory_path),
                "requireToken": ctx.config.require_token,
                "fullAccessEnabled": ctx.config.full_access_enabled,
                "supportsDynamicToolRegistration": True,
                "acceptedTokenHeaders": ctx.config.accepted_token_headers,
            },
        )
    )
    register(
        AiToolDefinition(
            id="tool.get_template",
            description="Return a safe template for a generated Photoshop bridge tool definition.",
            args_schema_json=schema_json({
                "type": "object",
                "additionalProperties": False,
                "properties": {
                    "toolId": {"type": "string"},
                    "description": {"type": "string"},
                    "fileName": {"type": "string"},
                },
            }),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="tool.get_template",
            handler=lambda args, ctx: ctx.generated_tool_host.get_template(
                tool_id=str(args.get("toolId") or "generated.photoshop_example"),
                description=str(args.get("description") or "Generated Photoshop tool."),
                file_name=str(args.get("fileName") or ""),
            ),
        )
    )
    register(
        AiToolDefinition(
            id="tool.list_generated",
            description="List generated Photoshop bridge tool definitions available to the adapter.",
            args_schema_json=schema_json({
                "type": "object",
                "additionalProperties": False,
                "properties": {
                    "pageSize": {"type": "integer", "minimum": 1, "maximum": 200},
                },
            }),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="tool.list_generated",
            handler=lambda args, ctx: ctx.result_handle_store.build_items_result(
                "tool.list_generated",
                "items",
                ctx.generated_tool_host.list_definitions(),
                {"kind": "generatedTools", "platformId": "photoshop"},
                clamp_number(int(args.get("pageSize") or 100), 1, 200),
            ),
        )
    )
    register(
        AiToolDefinition(
            id="tool.upsert_generated",
            description="Create or update one generated Photoshop bridge tool definition and reload the manifest.",
            args_schema_json=schema_json({
                "type": "object",
                "required": ["fileName", "definition"],
                "additionalProperties": False,
                "properties": {
                    "fileName": {"type": "string"},
                    "definition": {"type": "object"},
                },
            }),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="tool.upsert_generated",
            danger=AiToolDanger.HIGH,
            requires_confirmation=True,
            handler=lambda args, ctx: _upsert_generated_tool(args, ctx),
        )
    )
    register(
        AiToolDefinition(
            id="tool.delete_generated",
            description="Delete one generated Photoshop bridge tool definition and reload the manifest.",
            args_schema_json=schema_json({
                "type": "object",
                "required": ["fileName"],
                "additionalProperties": False,
                "properties": {
                    "fileName": {"type": "string"},
                },
            }),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="tool.delete_generated",
            danger=AiToolDanger.HIGH,
            requires_confirmation=True,
            handler=lambda args, ctx: _delete_generated_tool(args, ctx),
        )
    )
    register(
        AiToolDefinition(
            id="tool.reload_generated",
            description="Force a generated-tool rescan and manifest rebuild.",
            args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="tool.reload_generated",
            handler=lambda _args, ctx: ctx.reload_generated_tools(force=True),
        )
    )
    register(
        AiToolDefinition(
            id="service.logs_get",
            description="List recent service logs.",
            args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="service.logs_get",
            handler=lambda args, ctx: ctx.result_handle_store.build_items_result("service.logs_get", "logs", ctx.runtime_state.recent_logs(int(args.get("maxEntries") or 100)), {"kind": "logs"}, clamp_number(int(args.get("maxEntries") or 100), 1, 200)),
        )
    )
    register(
        AiToolDefinition(
            id="service.recent_tool_calls_get",
            description="List recent tool call outcomes recorded by the adapter.",
            args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="service.recent_tool_calls_get",
            handler=lambda args, ctx: ctx.result_handle_store.build_items_result("service.recent_tool_calls_get", "calls", ctx.runtime_state.recent_calls(int(args.get("maxEntries") or 100)), {"kind": "toolCalls"}, clamp_number(int(args.get("maxEntries") or 100), 1, 200)),
        )
    )
    register(
        AiToolDefinition(
            id="service.token_regenerate",
            description="Regenerate the adapter token and return the new token value.",
            args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="service.token_regenerate",
            danger=AiToolDanger.MEDIUM,
            requires_confirmation=True,
            handler=lambda _args, ctx: {"token": ctx.regenerate_token(), "acceptedTokenHeaders": ctx.config.accepted_token_headers},
        )
    )
    register(
        AiToolDefinition(
            id="photoshop.bridge_status_get",
            description="Return Photoshop bridge folder health and plugin heartbeat information.",
            args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="photoshop.bridge_status_get",
            handler=lambda _args, ctx: ctx.bridge_client.read_status(),
        )
    )
    register(
        AiToolDefinition(
            id="photoshop.documents_list",
            description="List open Photoshop documents through the UXP plugin bridge.",
            args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"pageSize": {"type": "integer", "minimum": 1, "maximum": 200}}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="photoshop.documents_list",
            handler=lambda args, ctx: ctx.result_handle_store.build_items_result(
                "photoshop.documents_list",
                "documents",
                list(ctx.bridge_client.call("photoshop.documents_list", {}, timeout_seconds=ctx.config.tool_timeout_seconds) or []),
                {"kind": "documents"},
                clamp_number(int(args.get("pageSize") or 100), 1, 200),
            ),
        )
    )
    register(
        AiToolDefinition(
            id="photoshop.active_document_get",
            description="Return summary information for the active Photoshop document.",
            args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="photoshop.active_document_get",
            handler=lambda _args, ctx: ctx.bridge_client.call("photoshop.active_document_get", {}, timeout_seconds=ctx.config.tool_timeout_seconds),
        )
    )
    register(
        AiToolDefinition(
            id="photoshop.layers_list",
            description="List layers from one Photoshop document.",
            args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"documentId": {"type": "integer"}, "pageSize": {"type": "integer", "minimum": 1, "maximum": 200}}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="photoshop.layers_list",
            handler=lambda args, ctx: ctx.result_handle_store.build_items_result(
                "photoshop.layers_list",
                "layers",
                list(ctx.bridge_client.call("photoshop.layers_list", {"documentId": args.get("documentId")}, timeout_seconds=ctx.config.tool_timeout_seconds) or []),
                {"kind": "layers", "documentId": args.get("documentId")},
                clamp_number(int(args.get("pageSize") or 100), 1, 200),
            ),
        )
    )
    register(
        AiToolDefinition(
            id="photoshop.layer_detail_get",
            description="Inspect one layer from a Photoshop document.",
            args_schema_json=schema_json({"type": "object", "required": ["layerId"], "additionalProperties": False, "properties": {"documentId": {"type": "integer"}, "layerId": {"type": "integer"}}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="photoshop.layer_detail_get",
            handler=lambda args, ctx: ctx.bridge_client.call(
                "photoshop.layer_detail_get",
                {"documentId": args.get("documentId"), "layerId": args.get("layerId")},
                timeout_seconds=ctx.config.tool_timeout_seconds,
            ),
        )
    )
    register(
        AiToolDefinition(
            id="photoshop.layer_visibility_set",
            description="Set visibility for one Photoshop layer.",
            args_schema_json=schema_json({"type": "object", "required": ["layerId", "visible"], "additionalProperties": False, "properties": {"documentId": {"type": "integer"}, "layerId": {"type": "integer"}, "visible": {"type": "boolean"}}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="photoshop.layer_visibility_set",
            danger=AiToolDanger.MEDIUM,
            requires_confirmation=True,
            handler=lambda args, ctx: ctx.bridge_client.call(
                "photoshop.layer_visibility_set",
                {"documentId": args.get("documentId"), "layerId": args.get("layerId"), "visible": args.get("visible")},
                timeout_seconds=ctx.config.tool_timeout_seconds,
            ),
        )
    )
    register(
        AiToolDefinition(
            id="photoshop.text_layer_set",
            description="Set text content for one Photoshop text layer.",
            args_schema_json=schema_json({"type": "object", "required": ["layerId", "contents"], "additionalProperties": False, "properties": {"documentId": {"type": "integer"}, "layerId": {"type": "integer"}, "contents": {"type": "string"}}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="photoshop.text_layer_set",
            danger=AiToolDanger.HIGH,
            requires_confirmation=True,
            handler=lambda args, ctx: ctx.bridge_client.call(
                "photoshop.text_layer_set",
                {"documentId": args.get("documentId"), "layerId": args.get("layerId"), "contents": args.get("contents")},
                timeout_seconds=ctx.config.tool_timeout_seconds,
            ),
        )
    )
    register(
        AiToolDefinition(
            id="photoshop.document_save",
            description="Save one Photoshop document.",
            args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"documentId": {"type": "integer"}}}),
            return_schema_json=schema_json({"type": "object"}),
            handler_name="photoshop.document_save",
            danger=AiToolDanger.HIGH,
            requires_confirmation=True,
            handler=lambda args, ctx: ctx.bridge_client.call(
                "photoshop.document_save",
                {"documentId": args.get("documentId")},
                timeout_seconds=ctx.config.tool_timeout_seconds,
            ),
        )
    )


def _upsert_generated_tool(args: dict[str, object], ctx: AiToolExecutionContext) -> dict[str, object]:
    result = ctx.generated_tool_host.upsert_definition(
        file_name=str(args.get("fileName") or ""),
        definition=args.get("definition"),
    )
    reload_result = ctx.reload_generated_tools(force=True)
    return {
        **result,
        "manifestHash": reload_result["manifestHash"],
        "generatedToolCount": reload_result["generatedToolCount"],
        "message": "Generated Photoshop bridge tool definition written and manifest reloaded.",
    }


def _delete_generated_tool(args: dict[str, object], ctx: AiToolExecutionContext) -> dict[str, object]:
    result = ctx.generated_tool_host.delete_definition(str(args.get("fileName") or ""))
    reload_result = ctx.reload_generated_tools(force=True)
    return {
        **result,
        "manifestHash": reload_result["manifestHash"],
        "generatedToolCount": reload_result["generatedToolCount"],
    }
