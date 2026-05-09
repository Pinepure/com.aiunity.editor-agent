from __future__ import annotations

from pathlib import Path
from typing import Any

from .maya_runner import probe_maya_version, run_maya_operation
from .models import AiManifestBundleDefinition, AiToolDanger, AiToolDefinition, AiToolExecutionContext, clamp_number, schema_json


def default_bundles() -> list[AiManifestBundleDefinition]:
    return [
        AiManifestBundleDefinition(id="service", description="Adapter health, logs, config, and recent calls.", prefixes=["service."]),
        AiManifestBundleDefinition(id="maya.inspect", description="Maya installation, scene inspection, node inspection, and attribute inspection tools.", prefixes=["maya.installation_", "maya.scene_", "maya.nodes_", "maya.node_", "maya.attr_get"]),
        AiManifestBundleDefinition(id="maya.mutate", description="Controlled Maya node and attribute mutation tools.", prefixes=["maya.attr_set", "maya.node_create", "maya.node_delete"]),
    ]


def register_default_tools(registry, context: AiToolExecutionContext) -> None:
    def register(**kwargs: Any) -> None:
        registry.register(AiToolDefinition(**kwargs))

    register(id="service.health_get", description="Return the adapter health payload.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.health_get", handler=lambda args, ctx: ctx.build_health_payload(include_ok=True))
    register(id="service.agent_brief_get", description="Return the concise operating brief for this adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.agent_brief_get", handler=lambda args, ctx: ctx.build_agent_brief_payload(include_ok=True))
    register(id="service.config_get", description="Return effective adapter configuration and current scene defaults.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.config_get", handler=lambda args, ctx: {"rootDir": str(ctx.config.root_dir), "requireToken": ctx.config.require_token, "fullAccessEnabled": ctx.config.full_access_enabled, "acceptedTokenHeaders": ctx.config.accepted_token_headers, "mayapyExecutable": ctx.config.mayapy_executable, "defaultSceneFile": _default_scene_file(ctx)})
    register(id="service.logs_get", description="List recent service logs.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.logs_get", handler=lambda args, ctx: ctx.result_handle_store.build_items_result(source_tool_id="service.logs_get", field_name="logs", items=ctx.runtime_state.recent_logs(int(args.get("maxEntries") or 100)), summary={"kind": "logs"}, page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200)))
    register(id="service.recent_tool_calls_get", description="List recent tool call outcomes recorded by the adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.recent_tool_calls_get", handler=lambda args, ctx: ctx.result_handle_store.build_items_result(source_tool_id="service.recent_tool_calls_get", field_name="calls", items=ctx.runtime_state.recent_calls(int(args.get("maxEntries") or 100)), summary={"kind": "toolCalls"}, page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200)))
    register(id="service.token_regenerate", description="Regenerate the adapter token and return the new token value.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.token_regenerate", danger=AiToolDanger.MEDIUM, requires_confirmation=True, handler=lambda args, ctx: {"token": ctx.regenerate_token(), "acceptedTokenHeaders": ctx.config.accepted_token_headers})
    register(id="maya.installation_get", description="Probe the mayapy executable and return version details.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="maya.installation_get", handler=lambda args, ctx: probe_maya_version(ctx.config.mayapy_executable))
    register(id="maya.scene_summary_get", description="Return summary information for the selected Maya scene file or a new scene.", args_schema_json=schema_json(_scene_file_args_schema()), return_schema_json=schema_json({"type": "object"}), handler_name="maya.scene_summary_get", handler=lambda args, ctx: _run_read_operation(ctx, "scene_summary", args))
    register(id="maya.nodes_list", description="List Maya nodes with optional type filtering.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"sceneFile": {"type": "string"}, "nodeType": {"type": "string"}, "dagOnly": {"type": "boolean"}, "longNames": {"type": "boolean"}, "scanLimit": {"type": "integer", "minimum": 1, "maximum": 20000}, "pageSize": {"type": "integer", "minimum": 1, "maximum": 200}}}), return_schema_json=schema_json({"type": "object"}), handler_name="maya.nodes_list", handler=lambda args, ctx: _paged_list_result(ctx, source_tool_id="maya.nodes_list", field_name="nodes", items=_run_read_operation(ctx, "nodes_list", args), summary={"kind": "nodes", "nodeType": args.get("nodeType", ""), "dagOnly": bool(args.get("dagOnly", False))}, page_size=int(args.get("pageSize") or 20)))
    register(id="maya.node_get", description="Return detailed metadata for one Maya node.", args_schema_json=schema_json({"type": "object", "required": ["nodeName"], "additionalProperties": False, "properties": {"sceneFile": {"type": "string"}, "nodeName": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="maya.node_get", handler=lambda args, ctx: _run_read_operation(ctx, "node_get", args))
    register(id="maya.attr_get", description="Return one Maya attribute value.", args_schema_json=schema_json({"type": "object", "required": ["nodeName", "attrName"], "additionalProperties": False, "properties": {"sceneFile": {"type": "string"}, "nodeName": {"type": "string"}, "attrName": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="maya.attr_get", handler=lambda args, ctx: _run_read_operation(ctx, "attr_get", args))
    register(id="maya.attr_set", description="Set one Maya attribute and save the result.", args_schema_json=schema_json({"type": "object", "required": ["nodeName", "attrName"], "additionalProperties": False, "properties": {"sceneFile": {"type": "string"}, "nodeName": {"type": "string"}, "attrName": {"type": "string"}, "value": {}, "values": {"type": "array"}, "outputSceneFile": {"type": "string"}, "inPlace": {"type": "boolean"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="maya.attr_set", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _run_write_operation(ctx, "attr_set", args))
    register(id="maya.node_create", description="Create a Maya node and save the result.", args_schema_json=schema_json({"type": "object", "required": ["nodeType"], "additionalProperties": False, "properties": {"sceneFile": {"type": "string"}, "nodeType": {"type": "string"}, "nodeName": {"type": "string"}, "parentNode": {"type": "string"}, "outputSceneFile": {"type": "string"}, "inPlace": {"type": "boolean"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="maya.node_create", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _run_write_operation(ctx, "node_create", args))
    register(id="maya.node_delete", description="Delete a Maya node and save the result.", args_schema_json=schema_json({"type": "object", "required": ["nodeName"], "additionalProperties": False, "properties": {"sceneFile": {"type": "string"}, "nodeName": {"type": "string"}, "outputSceneFile": {"type": "string"}, "inPlace": {"type": "boolean"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="maya.node_delete", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _run_write_operation(ctx, "node_delete", args))


def _default_scene_file(context: AiToolExecutionContext) -> str | None:
    path = _resolve_scene_file(context, {}, required=False)
    return str(path) if path else None


def _scene_file_args_schema() -> dict[str, Any]:
    return {"type": "object", "additionalProperties": False, "properties": {"sceneFile": {"type": "string"}}}


def _run_read_operation(context: AiToolExecutionContext, operation: str, args: dict[str, Any]) -> Any:
    scene_file = _resolve_scene_file(context, args, required=False)
    result = run_maya_operation(context.config, operation=operation, args=_normalize_args(context, args), scene_file=scene_file)
    return result["result"]


def _run_write_operation(context: AiToolExecutionContext, operation: str, args: dict[str, Any]) -> Any:
    scene_file = _resolve_scene_file(context, args, required=False)
    result = run_maya_operation(context.config, operation=operation, args=_normalize_args(context, args), scene_file=scene_file)
    return result["result"]


def _paged_list_result(context: AiToolExecutionContext, *, source_tool_id: str, field_name: str, items: list[Any], summary: dict[str, Any], page_size: int) -> dict[str, Any]:
    return context.result_handle_store.build_items_result(source_tool_id=source_tool_id, field_name=field_name, items=items, summary=summary, page_size=clamp_number(page_size or 20, 1, 200))


def _resolve_scene_file(context: AiToolExecutionContext, args: dict[str, Any], *, required: bool) -> Path | None:
    value = str(args.get("sceneFile") or context.config.default_scene_file or "").strip()
    if not value:
        if required:
            raise RuntimeError("This tool requires sceneFile or a configured default scene file.")
        return None
    path = _resolve_path(context.config.root_dir, value)
    if not path.exists():
        raise FileNotFoundError(f"Scene file does not exist: {path}")
    return path


def _normalize_args(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    normalized = dict(args)
    if normalized.get("sceneFile"):
        normalized["sceneFile"] = str(_resolve_path(context.config.root_dir, str(normalized["sceneFile"])))
    if normalized.get("outputSceneFile"):
        normalized["outputSceneFile"] = str(_resolve_path(context.config.root_dir, str(normalized["outputSceneFile"])))
    return normalized


def _resolve_path(root_dir: Path, value: str) -> Path:
    path = Path(value).expanduser()
    return path.resolve() if path.is_absolute() else (root_dir / path).resolve()
