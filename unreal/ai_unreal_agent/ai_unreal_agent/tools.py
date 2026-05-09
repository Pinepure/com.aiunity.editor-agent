from __future__ import annotations

import json
from pathlib import Path
from typing import Any
from urllib import error, request

from .models import (
    AiManifestBundleDefinition,
    AiToolDanger,
    AiToolDefinition,
    AiToolExecutionContext,
    clamp_number,
    schema_json,
)
from .unreal_runner import probe_unreal_installation, run_unreal_operation


def default_bundles() -> list[AiManifestBundleDefinition]:
    return [
        AiManifestBundleDefinition(
            id="service",
            description="Adapter health, logs, config, and recent calls.",
            prefixes=["service."],
        ),
        AiManifestBundleDefinition(
            id="unreal.inspect",
            description="Unreal installation, project inspection, asset inspection, and actor inspection tools.",
            prefixes=[
                "unreal.installation_",
                "unreal.project_",
                "unreal.assets_",
                "unreal.asset_",
                "unreal.level_actors_",
                "unreal.actor_get",
            ],
        ),
        AiManifestBundleDefinition(
            id="unreal.remote",
            description="Remote Control API connectivity and preset discovery tools.",
            prefixes=["unreal.remote_"],
        ),
        AiManifestBundleDefinition(
            id="unreal.mutate",
            description="Controlled Unreal asset and actor mutation tools.",
            prefixes=[
                "unreal.actor_set_",
                "unreal.asset_save",
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
        description="Return effective adapter configuration and default Unreal project state.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="service.config_get",
        handler=lambda args, ctx: {
            "rootDir": str(ctx.config.root_dir),
            "requireToken": ctx.config.require_token,
            "fullAccessEnabled": ctx.config.full_access_enabled,
            "acceptedTokenHeaders": ctx.config.accepted_token_headers,
            "unrealExecutable": ctx.config.unreal_executable,
            "projectFile": _default_project_file(ctx),
            "remoteControlUrl": ctx.config.remote_control_url,
        },
    )
    register(
        id="service.logs_get",
        description="List recent service logs.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}),
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
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}),
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
        id="unreal.installation_get",
        description="Resolve the configured Unreal executable, project file, and Remote Control base URL.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="unreal.installation_get",
        handler=lambda args, ctx: probe_unreal_installation(ctx.config),
    )
    register(
        id="unreal.remote_status_get",
        description="Probe the Unreal Remote Control HTTP API at the configured base URL.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="unreal.remote_status_get",
        handler=lambda args, ctx: _fetch_remote_json(ctx.config.remote_control_url, "/remote/info"),
    )
    register(
        id="unreal.remote_presets_list",
        description="List Remote Control presets exposed by the running Unreal instance.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="unreal.remote_presets_list",
        handler=lambda args, ctx: _fetch_remote_json(ctx.config.remote_control_url, "/remote/presets"),
    )
    register(
        id="unreal.project_summary_get",
        description="Return summary information for the configured Unreal project.",
        args_schema_json=schema_json(_project_file_args_schema()),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="unreal.project_summary_get",
        handler=lambda args, ctx: _run_read_operation(ctx, "project_summary", args),
    )
    register(
        id="unreal.assets_list",
        description="List content assets under one Unreal directory path with optional class filtering.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "additionalProperties": False,
                "properties": {
                    "projectFile": {"type": "string"},
                    "directoryPath": {"type": "string"},
                    "recursive": {"type": "boolean"},
                    "className": {"type": "string"},
                    "includeTags": {"type": "boolean"},
                    "scanLimit": {"type": "integer", "minimum": 1, "maximum": 20000},
                    "pageSize": {"type": "integer", "minimum": 1, "maximum": 200},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="unreal.assets_list",
        handler=lambda args, ctx: _paged_list_result(
            ctx,
            source_tool_id="unreal.assets_list",
            field_name="assets",
            items=_run_read_operation(ctx, "assets_list", args),
            summary={
                "kind": "assets",
                "directoryPath": args.get("directoryPath", "/Game"),
                "className": args.get("className", ""),
            },
            page_size=int(args.get("pageSize") or 20),
        ),
    )
    register(
        id="unreal.asset_get",
        description="Return detailed asset metadata for one Unreal asset path.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "required": ["assetPath"],
                "additionalProperties": False,
                "properties": {"projectFile": {"type": "string"}, "assetPath": {"type": "string"}},
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="unreal.asset_get",
        handler=lambda args, ctx: _run_read_operation(ctx, "asset_get", args),
    )
    register(
        id="unreal.level_actors_list",
        description="List level actors in the current editor world with optional class filtering.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "additionalProperties": False,
                "properties": {
                    "projectFile": {"type": "string"},
                    "className": {"type": "string"},
                    "scanLimit": {"type": "integer", "minimum": 1, "maximum": 20000},
                    "pageSize": {"type": "integer", "minimum": 1, "maximum": 200},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="unreal.level_actors_list",
        handler=lambda args, ctx: _paged_list_result(
            ctx,
            source_tool_id="unreal.level_actors_list",
            field_name="actors",
            items=_run_read_operation(ctx, "level_actors_list", args),
            summary={"kind": "actors", "className": args.get("className", "")},
            page_size=int(args.get("pageSize") or 20),
        ),
    )
    register(
        id="unreal.actor_get",
        description="Return detailed metadata for one level actor located by label or path.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "additionalProperties": False,
                "properties": {
                    "projectFile": {"type": "string"},
                    "actorLabel": {"type": "string"},
                    "actorPath": {"type": "string"},
                },
                "anyOf": [{"required": ["actorLabel"]}, {"required": ["actorPath"]}],
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="unreal.actor_get",
        handler=lambda args, ctx: _run_read_operation(ctx, "actor_get", args),
    )
    register(
        id="unreal.actor_set_label",
        description="Rename one Unreal level actor by label or path and optionally save the current level.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "required": ["newLabel"],
                "additionalProperties": False,
                "properties": {
                    "projectFile": {"type": "string"},
                    "actorLabel": {"type": "string"},
                    "actorPath": {"type": "string"},
                    "newLabel": {"type": "string"},
                    "saveCurrentLevel": {"type": "boolean"},
                },
                "anyOf": [{"required": ["actorLabel", "newLabel"]}, {"required": ["actorPath", "newLabel"]}],
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="unreal.actor_set_label",
        danger=AiToolDanger.HIGH,
        requires_confirmation=True,
        handler=lambda args, ctx: _run_write_operation(ctx, "actor_set_label", args),
    )
    register(
        id="unreal.actor_set_transform",
        description="Modify one Unreal level actor transform by label or path and optionally save the current level.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "additionalProperties": False,
                "properties": {
                    "projectFile": {"type": "string"},
                    "actorLabel": {"type": "string"},
                    "actorPath": {"type": "string"},
                    "location": {"type": "array", "items": {"type": "number"}, "minItems": 3, "maxItems": 3},
                    "rotation": {"type": "array", "items": {"type": "number"}, "minItems": 3, "maxItems": 3},
                    "scale": {"type": "array", "items": {"type": "number"}, "minItems": 3, "maxItems": 3},
                    "saveCurrentLevel": {"type": "boolean"},
                },
                "anyOf": [{"required": ["actorLabel"]}, {"required": ["actorPath"]}],
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="unreal.actor_set_transform",
        danger=AiToolDanger.HIGH,
        requires_confirmation=True,
        handler=lambda args, ctx: _run_write_operation(ctx, "actor_set_transform", args),
    )
    register(
        id="unreal.asset_save",
        description="Save one Unreal asset path, optionally only if it is dirty.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "required": ["assetPath"],
                "additionalProperties": False,
                "properties": {
                    "projectFile": {"type": "string"},
                    "assetPath": {"type": "string"},
                    "onlyIfDirty": {"type": "boolean"},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="unreal.asset_save",
        danger=AiToolDanger.HIGH,
        requires_confirmation=True,
        handler=lambda args, ctx: _run_write_operation(ctx, "asset_save", args),
    )


def _default_project_file(context: AiToolExecutionContext) -> str | None:
    path = _resolve_project_file(context, {}, required=False)
    return str(path) if path else None


def _project_file_args_schema() -> dict[str, Any]:
    return {"type": "object", "additionalProperties": False, "properties": {"projectFile": {"type": "string"}}}


def _run_read_operation(context: AiToolExecutionContext, operation: str, args: dict[str, Any]) -> Any:
    project_file = _resolve_project_file(context, args, required=True)
    result = run_unreal_operation(
        context.config,
        operation=operation,
        args=_normalize_args(context, args),
        project_file=project_file,
    )
    return result["result"]


def _run_write_operation(context: AiToolExecutionContext, operation: str, args: dict[str, Any]) -> Any:
    project_file = _resolve_project_file(context, args, required=True)
    result = run_unreal_operation(
        context.config,
        operation=operation,
        args=_normalize_args(context, args),
        project_file=project_file,
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


def _resolve_project_file(context: AiToolExecutionContext, args: dict[str, Any], *, required: bool) -> Path | None:
    value = str(args.get("projectFile") or context.config.project_file or "").strip()
    if not value:
        if required:
            raise RuntimeError("This tool requires projectFile or a configured default project file.")
        return None
    path = _resolve_path(context.config.root_dir, value)
    if not path.exists():
        raise FileNotFoundError(f"Project file does not exist: {path}")
    return path


def _normalize_args(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    normalized = dict(args)
    if normalized.get("projectFile"):
        normalized["projectFile"] = str(_resolve_path(context.config.root_dir, str(normalized["projectFile"])))
    return normalized


def _resolve_path(root_dir: Path, value: str) -> Path:
    path = Path(value).expanduser()
    return path.resolve() if path.is_absolute() else (root_dir / path).resolve()


def _fetch_remote_json(base_url: str, route: str) -> dict[str, Any]:
    url = f"{base_url.rstrip('/')}{route}"
    req = request.Request(url, headers={"Accept": "application/json"})
    try:
        with request.urlopen(req, timeout=10) as response:  # noqa: S310
            raw = response.read().decode("utf-8")
    except error.URLError as exc:
        raise RuntimeError(f"Remote Control request failed for {url}: {exc}") from exc
    return json.loads(raw) if raw.strip() else {}
