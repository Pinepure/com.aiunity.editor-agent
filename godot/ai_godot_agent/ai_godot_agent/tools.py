from __future__ import annotations

from pathlib import Path
from typing import Any

from .godot_runner import probe_godot_version, run_godot_operation
from .models import AiManifestBundleDefinition, AiToolDanger, AiToolDefinition, AiToolExecutionContext, clamp_number, schema_json


def default_bundles() -> list[AiManifestBundleDefinition]:
    return [
        AiManifestBundleDefinition(id="service", description="Adapter health, logs, config, and recent calls.", prefixes=["service."]),
        AiManifestBundleDefinition(
            id="godot.inspect",
            description="Godot installation, project inspection, and scene tree inspection tools.",
            prefixes=["godot.installation_", "godot.project_", "godot.scenes_", "godot.scene_nodes_", "godot.node_", "godot.node_property_get"],
        ),
        AiManifestBundleDefinition(
            id="godot.mutate",
            description="Controlled Godot scene mutation tools.",
            prefixes=["godot.node_property_set", "godot.scene_add_node", "godot.scene_delete_node"],
        ),
    ]


def register_default_tools(registry, context: AiToolExecutionContext) -> None:
    def register(**kwargs: Any) -> None:
        registry.register(AiToolDefinition(**kwargs))

    register(id="service.health_get", description="Return the adapter health payload.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.health_get", handler=lambda args, ctx: ctx.build_health_payload(include_ok=True))
    register(id="service.agent_brief_get", description="Return the concise operating brief for this adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.agent_brief_get", handler=lambda args, ctx: ctx.build_agent_brief_payload(include_ok=True))
    register(
        id="service.config_get",
        description="Return effective adapter configuration and current project defaults.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="service.config_get",
        handler=lambda args, ctx: {
            "rootDir": str(ctx.config.root_dir),
            "requireToken": ctx.config.require_token,
            "fullAccessEnabled": ctx.config.full_access_enabled,
            "acceptedTokenHeaders": ctx.config.accepted_token_headers,
            "godotExecutable": ctx.config.godot_executable,
            "projectDir": _default_project_dir(ctx),
        },
    )
    register(id="service.logs_get", description="List recent service logs.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.logs_get", handler=lambda args, ctx: ctx.result_handle_store.build_items_result(source_tool_id="service.logs_get", field_name="logs", items=ctx.runtime_state.recent_logs(int(args.get("maxEntries") or 100)), summary={"kind": "logs"}, page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200)))
    register(id="service.recent_tool_calls_get", description="List recent tool call outcomes recorded by the adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.recent_tool_calls_get", handler=lambda args, ctx: ctx.result_handle_store.build_items_result(source_tool_id="service.recent_tool_calls_get", field_name="calls", items=ctx.runtime_state.recent_calls(int(args.get("maxEntries") or 100)), summary={"kind": "toolCalls"}, page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200)))
    register(id="service.token_regenerate", description="Regenerate the adapter token and return the new token value.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.token_regenerate", danger=AiToolDanger.MEDIUM, requires_confirmation=True, handler=lambda args, ctx: {"token": ctx.regenerate_token(), "acceptedTokenHeaders": ctx.config.accepted_token_headers})
    register(id="godot.installation_get", description="Probe the Godot executable and return version details.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="godot.installation_get", handler=lambda args, ctx: probe_godot_version(ctx.config.godot_executable))
    register(id="godot.project_summary_get", description="Return summary information for the configured Godot project.", args_schema_json=schema_json(_project_dir_args_schema()), return_schema_json=schema_json({"type": "object"}), handler_name="godot.project_summary_get", handler=lambda args, ctx: _run_read_operation(ctx, "project_summary", args))
    register(id="godot.scenes_list", description="List scene files in the configured Godot project.", args_schema_json=schema_json(_project_dir_page_schema()), return_schema_json=schema_json({"type": "object"}), handler_name="godot.scenes_list", handler=lambda args, ctx: _paged_list_result(ctx, source_tool_id="godot.scenes_list", field_name="scenes", items=_run_read_operation(ctx, "scenes_list", args), summary={"kind": "scenes"}, page_size=int(args.get("pageSize") or 20)))
    register(
        id="godot.scene_nodes_list",
        description="List all nodes inside a Godot scene file.",
        args_schema_json=schema_json({"type": "object", "required": ["scenePath"], "additionalProperties": False, "properties": {"projectDir": {"type": "string"}, "scenePath": {"type": "string"}, "pageSize": {"type": "integer", "minimum": 1, "maximum": 200}}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="godot.scene_nodes_list",
        handler=lambda args, ctx: _paged_list_result(ctx, source_tool_id="godot.scene_nodes_list", field_name="nodes", items=_run_read_operation(ctx, "scene_nodes_list", args), summary={"kind": "sceneNodes", "scenePath": args.get("scenePath", "")}, page_size=int(args.get("pageSize") or 20)),
    )
    register(id="godot.node_get", description="Return metadata for one node inside a Godot scene.", args_schema_json=schema_json({"type": "object", "required": ["scenePath", "nodePath"], "additionalProperties": False, "properties": {"projectDir": {"type": "string"}, "scenePath": {"type": "string"}, "nodePath": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="godot.node_get", handler=lambda args, ctx: _run_read_operation(ctx, "node_get", args))
    register(id="godot.node_property_get", description="Read one property from a Godot scene node.", args_schema_json=schema_json({"type": "object", "required": ["scenePath", "nodePath", "propertyName"], "additionalProperties": False, "properties": {"projectDir": {"type": "string"}, "scenePath": {"type": "string"}, "nodePath": {"type": "string"}, "propertyName": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="godot.node_property_get", handler=lambda args, ctx: _run_read_operation(ctx, "node_property_get", args))
    register(id="godot.node_property_set", description="Set one property on a Godot scene node and save the scene.", args_schema_json=schema_json({"type": "object", "required": ["scenePath", "nodePath", "propertyName"], "additionalProperties": False, "properties": {"projectDir": {"type": "string"}, "scenePath": {"type": "string"}, "nodePath": {"type": "string"}, "propertyName": {"type": "string"}, "value": {}, "outputScenePath": {"type": "string"}, "inPlace": {"type": "boolean"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="godot.node_property_set", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _run_write_operation(ctx, "node_property_set", args))
    register(id="godot.scene_add_node", description="Create a child node inside a Godot scene and save the result.", args_schema_json=schema_json({"type": "object", "required": ["scenePath", "parentPath", "nodeClass"], "additionalProperties": False, "properties": {"projectDir": {"type": "string"}, "scenePath": {"type": "string"}, "parentPath": {"type": "string"}, "nodeClass": {"type": "string"}, "nodeName": {"type": "string"}, "outputScenePath": {"type": "string"}, "inPlace": {"type": "boolean"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="godot.scene_add_node", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _run_write_operation(ctx, "scene_add_node", args))
    register(id="godot.scene_delete_node", description="Delete a node from a Godot scene and save the result.", args_schema_json=schema_json({"type": "object", "required": ["scenePath", "nodePath"], "additionalProperties": False, "properties": {"projectDir": {"type": "string"}, "scenePath": {"type": "string"}, "nodePath": {"type": "string"}, "outputScenePath": {"type": "string"}, "inPlace": {"type": "boolean"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="godot.scene_delete_node", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _run_write_operation(ctx, "scene_delete_node", args))


def _default_project_dir(context: AiToolExecutionContext) -> str | None:
    path = _resolve_project_dir(context, {}, required=False)
    return str(path) if path else None


def _project_dir_args_schema() -> dict[str, Any]:
    return {"type": "object", "additionalProperties": False, "properties": {"projectDir": {"type": "string"}}}


def _project_dir_page_schema() -> dict[str, Any]:
    schema = _project_dir_args_schema()
    schema["properties"]["pageSize"] = {"type": "integer", "minimum": 1, "maximum": 200}
    return schema


def _run_read_operation(context: AiToolExecutionContext, operation: str, args: dict[str, Any]) -> Any:
    project_dir = _resolve_project_dir(context, args, required=True)
    result = run_godot_operation(context.config, operation=operation, args=_normalize_args(context, args), project_dir=project_dir)
    return result["result"]


def _run_write_operation(context: AiToolExecutionContext, operation: str, args: dict[str, Any]) -> Any:
    project_dir = _resolve_project_dir(context, args, required=True)
    result = run_godot_operation(context.config, operation=operation, args=_normalize_args(context, args), project_dir=project_dir)
    return result["result"]


def _paged_list_result(context: AiToolExecutionContext, *, source_tool_id: str, field_name: str, items: list[Any], summary: dict[str, Any], page_size: int) -> dict[str, Any]:
    return context.result_handle_store.build_items_result(source_tool_id=source_tool_id, field_name=field_name, items=items, summary=summary, page_size=clamp_number(page_size or 20, 1, 200))


def _resolve_project_dir(context: AiToolExecutionContext, args: dict[str, Any], *, required: bool) -> Path | None:
    value = str(args.get("projectDir") or context.config.project_dir or "").strip()
    if not value:
      if required:
        raise RuntimeError("This tool requires projectDir or a configured default project directory.")
      return None
    path = _resolve_path(context.config.root_dir, value)
    if not path.exists():
        raise FileNotFoundError(f"Project directory does not exist: {path}")
    if not (path / "project.godot").exists():
        raise FileNotFoundError(f"project.godot was not found under: {path}")
    return path


def _normalize_args(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    normalized = dict(args)
    if normalized.get("projectDir"):
        normalized["projectDir"] = str(_resolve_path(context.config.root_dir, str(normalized["projectDir"])))
    return normalized


def _resolve_path(root_dir: Path, value: str) -> Path:
    path = Path(value).expanduser()
    return path.resolve() if path.is_absolute() else (root_dir / path).resolve()
