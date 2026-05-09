from __future__ import annotations

from pathlib import Path
from typing import Any

from .houdini_runner import probe_houdini_version, run_houdini_operation
from .models import (
    AiManifestBundleDefinition,
    AiToolDanger,
    AiToolDefinition,
    AiToolExecutionContext,
    clamp_number,
    schema_json,
)


def default_bundles() -> list[AiManifestBundleDefinition]:
    return [
        AiManifestBundleDefinition(
            id="service",
            description="Adapter health, logs, config, and recent calls.",
            prefixes=["service."],
        ),
        AiManifestBundleDefinition(
            id="houdini.inspect",
            description="Houdini installation, hip file inspection, and node graph inspection tools.",
            prefixes=[
                "houdini.installation_",
                "houdini.file_",
                "houdini.root_networks_",
                "houdini.nodes_",
                "houdini.node_",
                "houdini.parm_get",
            ],
        ),
        AiManifestBundleDefinition(
            id="houdini.mutate",
            description="Controlled Houdini graph and parameter mutation tools.",
            prefixes=[
                "houdini.parm_set",
                "houdini.node_create",
                "houdini.node_delete",
            ],
        ),
    ]


def register_default_tools(registry, context: AiToolExecutionContext) -> None:
    def register(**kwargs: Any) -> None:
        registry.register(AiToolDefinition(**kwargs))

    register(
        id="service.health_get",
        description="Return the adapter health payload.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="service.health_get",
        handler=lambda args, ctx: ctx.build_health_payload(include_ok=True),
    )
    register(
        id="service.agent_brief_get",
        description="Return the concise operating brief for this adapter.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="service.agent_brief_get",
        handler=lambda args, ctx: ctx.build_agent_brief_payload(include_ok=True),
    )
    register(
        id="service.config_get",
        description="Return effective adapter configuration and current hip defaults.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="service.config_get",
        handler=lambda args, ctx: {
            "rootDir": str(ctx.config.root_dir),
            "requireToken": ctx.config.require_token,
            "fullAccessEnabled": ctx.config.full_access_enabled,
            "acceptedTokenHeaders": ctx.config.accepted_token_headers,
            "hythonExecutable": ctx.config.hython_executable,
            "defaultHipFile": _default_hip_file(ctx),
        },
    )
    register(
        id="service.logs_get",
        description="List recent service logs.",
        args_schema_json=schema_json(
            {"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="service.logs_get",
        handler=lambda args, ctx: ctx.result_handle_store.build_items_result(
            source_tool_id="service.logs_get",
            field_name="logs",
            items=ctx.runtime_state.recent_logs(int(args.get("maxEntries") or 100)),
            summary={"kind": "logs"},
            page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200),
        ),
    )
    register(
        id="service.recent_tool_calls_get",
        description="List recent tool call outcomes recorded by the adapter.",
        args_schema_json=schema_json(
            {"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="service.recent_tool_calls_get",
        handler=lambda args, ctx: ctx.result_handle_store.build_items_result(
            source_tool_id="service.recent_tool_calls_get",
            field_name="calls",
            items=ctx.runtime_state.recent_calls(int(args.get("maxEntries") or 100)),
            summary={"kind": "toolCalls"},
            page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200),
        ),
    )
    register(
        id="service.token_regenerate",
        description="Regenerate the adapter token and return the new token value.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="service.token_regenerate",
        danger=AiToolDanger.MEDIUM,
        requires_confirmation=True,
        handler=lambda args, ctx: {"token": ctx.regenerate_token(), "acceptedTokenHeaders": ctx.config.accepted_token_headers},
    )
    register(
        id="houdini.installation_get",
        description="Probe the hython executable and return version details.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="houdini.installation_get",
        handler=lambda args, ctx: probe_houdini_version(ctx.config.hython_executable),
    )
    register(
        id="houdini.file_summary_get",
        description="Return a high-level summary for the selected .hip file or a clean Houdini session.",
        args_schema_json=schema_json(_hip_file_args_schema()),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="houdini.file_summary_get",
        handler=lambda args, ctx: _run_read_operation(ctx, "file_summary", args),
    )
    register(
        id="houdini.root_networks_list",
        description="List the top-level Houdini networks from the selected .hip file.",
        args_schema_json=schema_json(_hip_file_page_schema()),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="houdini.root_networks_list",
        handler=lambda args, ctx: _paged_list_result(
            ctx,
            source_tool_id="houdini.root_networks_list",
            field_name="networks",
            items=_run_read_operation(ctx, "root_networks_list", args),
            summary={"kind": "rootNetworks"},
            page_size=int(args.get("pageSize") or 20),
        ),
    )
    register(
        id="houdini.nodes_list",
        description="List child or descendant nodes under one Houdini network path.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "additionalProperties": False,
                "properties": {
                    "hipFile": {"type": "string"},
                    "rootPath": {"type": "string"},
                    "recursive": {"type": "boolean"},
                    "nodeTypeName": {"type": "string"},
                    "scanLimit": {"type": "integer", "minimum": 1, "maximum": 20000},
                    "pageSize": {"type": "integer", "minimum": 1, "maximum": 200},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="houdini.nodes_list",
        handler=lambda args, ctx: _paged_list_result(
            ctx,
            source_tool_id="houdini.nodes_list",
            field_name="nodes",
            items=_run_read_operation(ctx, "nodes_list", args),
            summary={
                "kind": "nodes",
                "rootPath": args.get("rootPath", "/"),
                "recursive": bool(args.get("recursive", False)),
                "nodeTypeName": args.get("nodeTypeName", ""),
            },
            page_size=int(args.get("pageSize") or 20),
        ),
    )
    register(
        id="houdini.node_children_list",
        description="List direct child nodes for one Houdini network path.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "additionalProperties": False,
                "properties": {
                    "hipFile": {"type": "string"},
                    "nodePath": {"type": "string"},
                    "pageSize": {"type": "integer", "minimum": 1, "maximum": 200},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="houdini.node_children_list",
        handler=lambda args, ctx: _paged_list_result(
            ctx,
            source_tool_id="houdini.node_children_list",
            field_name="nodes",
            items=_run_read_operation(ctx, "node_children_list", args),
            summary={"kind": "nodeChildren", "nodePath": args.get("nodePath", "/")},
            page_size=int(args.get("pageSize") or 20),
        ),
    )
    register(
        id="houdini.node_get",
        description="Return detailed metadata for one Houdini node path.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "required": ["nodePath"],
                "additionalProperties": False,
                "properties": {"hipFile": {"type": "string"}, "nodePath": {"type": "string"}},
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="houdini.node_get",
        handler=lambda args, ctx: _run_read_operation(ctx, "node_get", args),
    )
    register(
        id="houdini.parm_get",
        description="Return one Houdini parameter value or tuple value by node path and parameter name.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "required": ["nodePath", "parmName"],
                "additionalProperties": False,
                "properties": {
                    "hipFile": {"type": "string"},
                    "nodePath": {"type": "string"},
                    "parmName": {"type": "string"},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="houdini.parm_get",
        handler=lambda args, ctx: _run_read_operation(ctx, "parm_get", args),
    )
    register(
        id="houdini.parm_set",
        description="Modify one Houdini parameter or parameter tuple and save the result.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "required": ["nodePath", "parmName"],
                "additionalProperties": False,
                "properties": {
                    "hipFile": {"type": "string"},
                    "nodePath": {"type": "string"},
                    "parmName": {"type": "string"},
                    "value": {},
                    "values": {"type": "array"},
                    "outputHipFile": {"type": "string"},
                    "inPlace": {"type": "boolean"},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="houdini.parm_set",
        danger=AiToolDanger.HIGH,
        requires_confirmation=True,
        handler=lambda args, ctx: _run_write_operation(ctx, "parm_set", args),
    )
    register(
        id="houdini.node_create",
        description="Create a Houdini node under a parent network path and save the result.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "required": ["parentPath", "nodeTypeName"],
                "additionalProperties": False,
                "properties": {
                    "hipFile": {"type": "string"},
                    "parentPath": {"type": "string"},
                    "nodeTypeName": {"type": "string"},
                    "nodeName": {"type": "string"},
                    "autoPosition": {"type": "boolean"},
                    "outputHipFile": {"type": "string"},
                    "inPlace": {"type": "boolean"},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="houdini.node_create",
        danger=AiToolDanger.HIGH,
        requires_confirmation=True,
        handler=lambda args, ctx: _run_write_operation(ctx, "node_create", args),
    )
    register(
        id="houdini.node_delete",
        description="Delete a Houdini node by path and save the result.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "required": ["nodePath"],
                "additionalProperties": False,
                "properties": {
                    "hipFile": {"type": "string"},
                    "nodePath": {"type": "string"},
                    "outputHipFile": {"type": "string"},
                    "inPlace": {"type": "boolean"},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="houdini.node_delete",
        danger=AiToolDanger.HIGH,
        requires_confirmation=True,
        handler=lambda args, ctx: _run_write_operation(ctx, "node_delete", args),
    )


def _default_hip_file(context: AiToolExecutionContext) -> str | None:
    path = _resolve_hip_file(context, {}, required=False)
    return str(path) if path else None


def _hip_file_args_schema() -> dict[str, Any]:
    return {"type": "object", "additionalProperties": False, "properties": {"hipFile": {"type": "string"}}}


def _hip_file_page_schema() -> dict[str, Any]:
    schema = _hip_file_args_schema()
    schema["properties"]["pageSize"] = {"type": "integer", "minimum": 1, "maximum": 200}
    return schema


def _run_read_operation(context: AiToolExecutionContext, operation: str, args: dict[str, Any]) -> Any:
    hip_file = _resolve_hip_file(context, args, required=False)
    result = run_houdini_operation(
        context.config,
        operation=operation,
        args=_normalize_args(context, args, write_mode=False),
        hip_file=hip_file,
    )
    return result["result"]


def _run_write_operation(context: AiToolExecutionContext, operation: str, args: dict[str, Any]) -> Any:
    hip_file = _resolve_hip_file(context, args, required=False)
    result = run_houdini_operation(
        context.config,
        operation=operation,
        args=_normalize_args(context, args, write_mode=True),
        hip_file=hip_file,
    )
    return result["result"]


def _paged_list_result(
    context: AiToolExecutionContext,
    *,
    source_tool_id: str,
    field_name: str,
    items: list[Any],
    summary: dict[str, Any],
    page_size: int,
) -> dict[str, Any]:
    return context.result_handle_store.build_items_result(
        source_tool_id=source_tool_id,
        field_name=field_name,
        items=items,
        summary=summary,
        page_size=clamp_number(page_size or 20, 1, 200),
    )


def _resolve_hip_file(context: AiToolExecutionContext, args: dict[str, Any], *, required: bool) -> Path | None:
    value = str(args.get("hipFile") or context.config.default_hip_file or "").strip()
    if not value:
        if required:
            raise RuntimeError("This tool requires hipFile or a configured default hip file.")
        return None
    path = _resolve_path(context.config.root_dir, value)
    if not path.exists():
        raise FileNotFoundError(f"Hip file does not exist: {path}")
    return path


def _normalize_args(context: AiToolExecutionContext, args: dict[str, Any], *, write_mode: bool) -> dict[str, Any]:
    normalized = dict(args)
    if normalized.get("hipFile"):
        normalized["hipFile"] = str(_resolve_path(context.config.root_dir, str(normalized["hipFile"])))
    if write_mode and normalized.get("outputHipFile"):
        normalized["outputHipFile"] = str(_resolve_path(context.config.root_dir, str(normalized["outputHipFile"])))
    return normalized


def _resolve_path(root_dir: Path, value: str) -> Path:
    path = Path(value).expanduser()
    return path.resolve() if path.is_absolute() else (root_dir / path).resolve()
