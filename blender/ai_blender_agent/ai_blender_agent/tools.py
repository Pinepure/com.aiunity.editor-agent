from __future__ import annotations

from pathlib import Path
from typing import Any

from .blender_runner import probe_blender_version, run_blender_operation
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
            id="blender.inspect",
            description="Blender installation, file inspection, scene discovery, and object inspection tools.",
            prefixes=[
                "blender.installation_",
                "blender.file_",
                "blender.scene_",
                "blender.scenes_",
                "blender.collections_",
                "blender.objects_",
                "blender.object_get",
            ],
        ),
        AiManifestBundleDefinition(
            id="blender.mutate",
            description="Controlled Blender mutation and rendering tools.",
            prefixes=[
                "blender.object_transform_",
                "blender.render_",
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
        description="Return effective adapter configuration and current file defaults.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="service.config_get",
        handler=lambda args, ctx: {
            "rootDir": str(ctx.config.root_dir),
            "requireToken": ctx.config.require_token,
            "fullAccessEnabled": ctx.config.full_access_enabled,
            "acceptedTokenHeaders": ctx.config.accepted_token_headers,
            "blenderExecutable": ctx.config.blender_executable,
            "defaultBlendFile": _default_blend_file(ctx),
        },
    )

    register(
        id="service.logs_get",
        description="List recent service logs.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "additionalProperties": False,
                "properties": {
                    "maxEntries": {"type": "integer", "minimum": 1, "maximum": 300},
                },
            }
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
            {
                "type": "object",
                "additionalProperties": False,
                "properties": {
                    "maxEntries": {"type": "integer", "minimum": 1, "maximum": 300},
                },
            }
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
        handler=lambda args, ctx: {
            "token": ctx.regenerate_token(),
            "acceptedTokenHeaders": ctx.config.accepted_token_headers,
        },
    )

    register(
        id="blender.installation_get",
        description="Probe the Blender executable and return version details.",
        args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="blender.installation_get",
        handler=lambda args, ctx: probe_blender_version(ctx.config.blender_executable),
    )

    register(
        id="blender.file_summary_get",
        description="Return a high-level summary for the selected .blend file.",
        args_schema_json=schema_json(_blend_file_args_schema()),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="blender.file_summary_get",
        handler=lambda args, ctx: _run_read_operation(ctx, "file_summary", args),
    )

    register(
        id="blender.scene_summary_get",
        description="Return summary information for one scene.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "additionalProperties": False,
                "properties": {
                    "blendFile": {"type": "string"},
                    "scene": {"type": "string"},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="blender.scene_summary_get",
        handler=lambda args, ctx: _run_read_operation(ctx, "scene_summary_get", args),
    )

    register(
        id="blender.scenes_list",
        description="List scenes from the selected .blend file.",
        args_schema_json=schema_json(_blend_file_args_schema()),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="blender.scenes_list",
        handler=lambda args, ctx: _paged_list_result(
            ctx,
            source_tool_id="blender.scenes_list",
            field_name="scenes",
            items=_run_read_operation(ctx, "scenes_list", args),
            summary={"kind": "scenes"},
            page_size=int(args.get("pageSize") or 20),
        ),
    )

    register(
        id="blender.collections_list",
        description="List collections from the selected .blend file.",
        args_schema_json=schema_json(_blend_file_args_schema()),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="blender.collections_list",
        handler=lambda args, ctx: _paged_list_result(
            ctx,
            source_tool_id="blender.collections_list",
            field_name="collections",
            items=_run_read_operation(ctx, "collections_list", args),
            summary={"kind": "collections"},
            page_size=int(args.get("pageSize") or 20),
        ),
    )

    register(
        id="blender.objects_list",
        description="List objects from the selected .blend file with optional scene, collection, and type filters.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "additionalProperties": False,
                "properties": {
                    "blendFile": {"type": "string"},
                    "scene": {"type": "string"},
                    "collection": {"type": "string"},
                    "type": {"type": "string"},
                    "includeHidden": {"type": "boolean"},
                    "scanLimit": {"type": "integer", "minimum": 1, "maximum": 20000},
                    "pageSize": {"type": "integer", "minimum": 1, "maximum": 200},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="blender.objects_list",
        handler=lambda args, ctx: _paged_list_result(
            ctx,
            source_tool_id="blender.objects_list",
            field_name="objects",
            items=_run_read_operation(ctx, "objects_list", args),
            summary={
                "kind": "objects",
                "scene": args.get("scene", ""),
                "collection": args.get("collection", ""),
                "type": args.get("type", ""),
            },
            page_size=int(args.get("pageSize") or 20),
        ),
    )

    register(
        id="blender.object_get",
        description="Return detailed metadata for one object in the selected .blend file.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "required": ["objectName"],
                "additionalProperties": False,
                "properties": {
                    "blendFile": {"type": "string"},
                    "objectName": {"type": "string"},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="blender.object_get",
        handler=lambda args, ctx: _run_read_operation(ctx, "object_get", args),
    )

    register(
        id="blender.object_transform_set",
        description="Modify one object's transform and save the result back to the source file or a new .blend file.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "required": ["objectName"],
                "additionalProperties": False,
                "properties": {
                    "blendFile": {"type": "string"},
                    "objectName": {"type": "string"},
                    "location": {"type": "array", "items": {"type": "number"}, "minItems": 3, "maxItems": 3},
                    "rotationEulerDegrees": {
                        "type": "array",
                        "items": {"type": "number"},
                        "minItems": 3,
                        "maxItems": 3,
                    },
                    "scale": {"type": "array", "items": {"type": "number"}, "minItems": 3, "maxItems": 3},
                    "outputBlendFile": {"type": "string"},
                    "inPlace": {"type": "boolean"},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="blender.object_transform_set",
        danger=AiToolDanger.HIGH,
        requires_confirmation=True,
        handler=lambda args, ctx: _run_write_operation(ctx, "object_transform_set", args),
    )

    register(
        id="blender.render_still",
        description="Render a still image from the selected .blend file to an output path.",
        args_schema_json=schema_json(
            {
                "type": "object",
                "required": ["outputPath"],
                "additionalProperties": False,
                "properties": {
                    "blendFile": {"type": "string"},
                    "scene": {"type": "string"},
                    "frame": {"type": "integer"},
                    "resolutionX": {"type": "integer", "minimum": 1},
                    "resolutionY": {"type": "integer", "minimum": 1},
                    "outputPath": {"type": "string"},
                },
            }
        ),
        return_schema_json=schema_json({"type": "object"}),
        handler_name="blender.render_still",
        danger=AiToolDanger.HIGH,
        requires_confirmation=True,
        handler=lambda args, ctx: _run_write_operation(ctx, "render_still", args),
    )


def _default_blend_file(context: AiToolExecutionContext) -> str | None:
    return str(_resolve_blend_file(context, {})) if _resolve_blend_file(context, {}, required=False) else None


def _blend_file_args_schema() -> dict[str, Any]:
    return {
        "type": "object",
        "additionalProperties": False,
        "properties": {
            "blendFile": {"type": "string"},
            "pageSize": {"type": "integer", "minimum": 1, "maximum": 200},
        },
    }


def _run_read_operation(context: AiToolExecutionContext, operation: str, args: dict[str, Any]) -> Any:
    blend_file = _resolve_blend_file(context, args, required=False)
    result = run_blender_operation(
        context.config,
        operation=operation,
        args=_normalize_args(context, args, write_mode=False),
        blend_file=blend_file,
    )
    return result["result"]


def _run_write_operation(context: AiToolExecutionContext, operation: str, args: dict[str, Any]) -> Any:
    blend_file = _resolve_blend_file(context, args, required=False)
    normalized_args = _normalize_args(context, args, write_mode=True)
    result = run_blender_operation(
        context.config,
        operation=operation,
        args=normalized_args,
        blend_file=blend_file,
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


def _resolve_blend_file(
    context: AiToolExecutionContext,
    args: dict[str, Any],
    *,
    required: bool = False,
) -> Path | None:
    value = str(args.get("blendFile") or context.config.default_blend_file or "").strip()
    if not value:
        if required:
            raise RuntimeError("This tool requires blendFile or a configured default blend file.")
        return None
    path = _resolve_path(context.config.root_dir, value)
    if not path.exists():
        raise FileNotFoundError(f"Blend file does not exist: {path}")
    return path


def _normalize_args(context: AiToolExecutionContext, args: dict[str, Any], *, write_mode: bool) -> dict[str, Any]:
    normalized = dict(args)
    if "blendFile" in normalized and normalized["blendFile"]:
        normalized["blendFile"] = str(_resolve_path(context.config.root_dir, str(normalized["blendFile"])))
    if write_mode and normalized.get("outputBlendFile"):
        normalized["outputBlendFile"] = str(
            _resolve_path(context.config.root_dir, str(normalized["outputBlendFile"]))
        )
    if write_mode and normalized.get("outputPath"):
        normalized["outputPath"] = str(_resolve_path(context.config.root_dir, str(normalized["outputPath"])))
    return normalized


def _resolve_path(root_dir: Path, value: str) -> Path:
    path = Path(value).expanduser()
    if path.is_absolute():
        return path.resolve()
    return (root_dir / path).resolve()
