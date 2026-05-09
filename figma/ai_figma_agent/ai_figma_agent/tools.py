from __future__ import annotations

from typing import Any

from .figma_client import figma_request
from .models import AiManifestBundleDefinition, AiToolDanger, AiToolDefinition, AiToolExecutionContext, clamp_number, schema_json


def default_bundles() -> list[AiManifestBundleDefinition]:
    return [
        AiManifestBundleDefinition(id="service", description="Adapter health, logs, config, and recent calls.", prefixes=["service."]),
        AiManifestBundleDefinition(id="figma.auth", description="Figma API token status and token persistence tools.", prefixes=["figma.token_", "figma.me_"]),
        AiManifestBundleDefinition(id="figma.files", description="Figma file, node, image, and metadata inspection tools.", prefixes=["figma.file_"]),
        AiManifestBundleDefinition(id="figma.comments", description="Figma comment inspection and mutation tools.", prefixes=["figma.comment", "figma.comments_"]),
        AiManifestBundleDefinition(id="figma.variables", description="Figma local and published variables tools.", prefixes=["figma.variables_"]),
    ]


def register_default_tools(registry, context: AiToolExecutionContext) -> None:
    def register(**kwargs: Any) -> None:
        registry.register(AiToolDefinition(**kwargs))

    register(id="service.health_get", description="Return the adapter health payload.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.health_get", handler=lambda args, ctx: ctx.build_health_payload(include_ok=True))
    register(id="service.agent_brief_get", description="Return the concise operating brief for this adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.agent_brief_get", handler=lambda args, ctx: ctx.build_agent_brief_payload(include_ok=True))
    register(id="service.config_get", description="Return effective adapter configuration and token status.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.config_get", handler=lambda args, ctx: {"rootDir": str(ctx.config.root_dir), "requireToken": ctx.config.require_token, "fullAccessEnabled": ctx.config.full_access_enabled, "acceptedTokenHeaders": ctx.config.accepted_token_headers, "figmaBaseUrl": ctx.config.figma_base_url, "figmaApiToken": ctx.describe_figma_api_token()})
    register(id="service.logs_get", description="List recent service logs.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.logs_get", handler=lambda args, ctx: ctx.result_handle_store.build_items_result(source_tool_id="service.logs_get", field_name="logs", items=ctx.runtime_state.recent_logs(int(args.get("maxEntries") or 100)), summary={"kind": "logs"}, page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200)))
    register(id="service.recent_tool_calls_get", description="List recent tool call outcomes recorded by the adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.recent_tool_calls_get", handler=lambda args, ctx: ctx.result_handle_store.build_items_result(source_tool_id="service.recent_tool_calls_get", field_name="calls", items=ctx.runtime_state.recent_calls(int(args.get("maxEntries") or 100)), summary={"kind": "toolCalls"}, page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200)))
    register(id="service.token_regenerate", description="Regenerate the adapter token and return the new token value.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.token_regenerate", danger=AiToolDanger.MEDIUM, requires_confirmation=True, handler=lambda args, ctx: {"token": ctx.regenerate_token(), "acceptedTokenHeaders": ctx.config.accepted_token_headers})
    register(id="figma.token_status_get", description="Return whether a Figma API token is currently available to the adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.token_status_get", handler=lambda args, ctx: {**ctx.describe_figma_api_token(), "baseUrl": ctx.config.figma_base_url})
    register(id="figma.token_set", description="Persist a Figma API token for later calls by this adapter.", args_schema_json=schema_json({"type": "object", "required": ["token"], "additionalProperties": False, "properties": {"token": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.token_set", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _set_token(ctx, str(args.get("token") or "")))
    register(id="figma.token_clear", description="Delete the persisted Figma API token file used by this adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.token_clear", danger=AiToolDanger.MEDIUM, requires_confirmation=True, handler=lambda args, ctx: _clear_token(ctx))
    register(id="figma.me_get", description="Return the authenticated Figma user profile from GET /v1/me.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.me_get", handler=lambda args, ctx: _api_get(ctx, "/v1/me"))
    register(id="figma.file_get", description="Return the JSON document for one Figma file.", args_schema_json=schema_json({"type": "object", "required": ["fileKey"], "additionalProperties": False, "properties": {"fileKey": {"type": "string"}, "version": {"type": "string"}, "depth": {"type": "integer", "minimum": 1}, "geometry": {"type": "string"}, "pluginData": {"anyOf": [{"type": "string"}, {"type": "array", "items": {"type": "string"}}]}, "branchData": {"type": "boolean"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.file_get", handler=lambda args, ctx: _api_get(ctx, f"/v1/files/{_require_string(args, 'fileKey')}", query={"version": args.get("version"), "depth": args.get("depth"), "geometry": args.get("geometry"), "plugin_data": _csv_value(args.get("pluginData")), "branch_data": args.get("branchData")}))
    register(id="figma.file_nodes_get", description="Return selected node JSON from one Figma file.", args_schema_json=schema_json({"type": "object", "required": ["fileKey", "nodeIds"], "additionalProperties": False, "properties": {"fileKey": {"type": "string"}, "nodeIds": {"anyOf": [{"type": "string"}, {"type": "array", "items": {"type": "string"}}]}, "version": {"type": "string"}, "depth": {"type": "integer", "minimum": 1}, "geometry": {"type": "string"}, "pluginData": {"anyOf": [{"type": "string"}, {"type": "array", "items": {"type": "string"}}]}}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.file_nodes_get", handler=lambda args, ctx: _api_get(ctx, f"/v1/files/{_require_string(args, 'fileKey')}/nodes", query={"ids": _csv_required(args.get("nodeIds"), "nodeIds"), "version": args.get("version"), "depth": args.get("depth"), "geometry": args.get("geometry"), "plugin_data": _csv_value(args.get("pluginData"))}))
    register(id="figma.file_meta_get", description="Return metadata for one Figma file.", args_schema_json=schema_json({"type": "object", "required": ["fileKey"], "additionalProperties": False, "properties": {"fileKey": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.file_meta_get", handler=lambda args, ctx: _api_get(ctx, f"/v1/files/{_require_string(args, 'fileKey')}/meta"))
    register(id="figma.file_images_get", description="Return rendered image URLs for selected Figma nodes.", args_schema_json=schema_json({"type": "object", "required": ["fileKey", "nodeIds"], "additionalProperties": False, "properties": {"fileKey": {"type": "string"}, "nodeIds": {"anyOf": [{"type": "string"}, {"type": "array", "items": {"type": "string"}}]}, "format": {"type": "string"}, "scale": {"type": "number"}, "version": {"type": "string"}, "useAbsoluteBounds": {"type": "boolean"}, "svgOutlineText": {"type": "boolean"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.file_images_get", handler=lambda args, ctx: _api_get(ctx, f"/v1/images/{_require_string(args, 'fileKey')}", query={"ids": _csv_required(args.get("nodeIds"), "nodeIds"), "format": args.get("format"), "scale": args.get("scale"), "version": args.get("version"), "use_absolute_bounds": args.get("useAbsoluteBounds"), "svg_outline_text": args.get("svgOutlineText")}))
    register(id="figma.file_image_fills_get", description="Return expiring image-fill URLs for one Figma file.", args_schema_json=schema_json({"type": "object", "required": ["fileKey"], "additionalProperties": False, "properties": {"fileKey": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.file_image_fills_get", handler=lambda args, ctx: _api_get(ctx, f"/v1/files/{_require_string(args, 'fileKey')}/images"))
    register(id="figma.comments_list", description="List comments for one Figma file.", args_schema_json=schema_json({"type": "object", "required": ["fileKey"], "additionalProperties": False, "properties": {"fileKey": {"type": "string"}, "asMarkdown": {"type": "boolean"}, "pageSize": {"type": "integer", "minimum": 1, "maximum": 200}}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.comments_list", handler=lambda args, ctx: _paged_comments(ctx, args))
    register(id="figma.comment_create", description="Create a root comment or reply in one Figma file.", args_schema_json=schema_json({"type": "object", "required": ["fileKey", "message"], "additionalProperties": False, "properties": {"fileKey": {"type": "string"}, "message": {"type": "string"}, "commentId": {"type": "string"}, "clientMeta": {"type": "object"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.comment_create", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _api_post(ctx, f"/v1/files/{_require_string(args, 'fileKey')}/comments", body=_comment_create_body(args)))
    register(id="figma.comment_delete", description="Delete a comment from one Figma file.", args_schema_json=schema_json({"type": "object", "required": ["fileKey", "commentId"], "additionalProperties": False, "properties": {"fileKey": {"type": "string"}, "commentId": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.comment_delete", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _api_delete(ctx, f"/v1/files/{_require_string(args, 'fileKey')}/comments/{_require_string(args, 'commentId')}"))
    register(id="figma.variables_local_get", description="Return local and referenced remote variables for one Figma file.", args_schema_json=schema_json({"type": "object", "required": ["fileKey"], "additionalProperties": False, "properties": {"fileKey": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.variables_local_get", handler=lambda args, ctx: _api_get(ctx, f"/v1/files/{_require_string(args, 'fileKey')}/variables/local"))
    register(id="figma.variables_published_get", description="Return published variables metadata for one Figma file.", args_schema_json=schema_json({"type": "object", "required": ["fileKey"], "additionalProperties": False, "properties": {"fileKey": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.variables_published_get", handler=lambda args, ctx: _api_get(ctx, f"/v1/files/{_require_string(args, 'fileKey')}/variables/published"))
    register(id="figma.variables_mutate", description="Bulk create, update, or delete local variables and collections in one Figma file.", args_schema_json=schema_json({"type": "object", "required": ["fileKey", "payload"], "additionalProperties": False, "properties": {"fileKey": {"type": "string"}, "payload": {"type": "object"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="figma.variables_mutate", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _api_post(ctx, f"/v1/files/{_require_string(args, 'fileKey')}/variables", body=_require_object(args, "payload")))


def _set_token(context: AiToolExecutionContext, token: str) -> dict[str, Any]:
    context.write_figma_api_token(token)
    return {**context.describe_figma_api_token(), "baseUrl": context.config.figma_base_url}


def _clear_token(context: AiToolExecutionContext) -> dict[str, Any]:
    context.clear_figma_api_token()
    return {**context.describe_figma_api_token(), "baseUrl": context.config.figma_base_url}


def _api_get(context: AiToolExecutionContext, path: str, *, query: dict[str, Any] | None = None) -> Any:
    return figma_request(context.config, token=_require_figma_token(context), method="GET", path=path, query=query)


def _api_post(context: AiToolExecutionContext, path: str, *, body: dict[str, Any]) -> Any:
    return figma_request(context.config, token=_require_figma_token(context), method="POST", path=path, body=body)


def _api_delete(context: AiToolExecutionContext, path: str) -> Any:
    return figma_request(context.config, token=_require_figma_token(context), method="DELETE", path=path)


def _paged_comments(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    file_key = _require_string(args, "fileKey")
    payload = _api_get(context, f"/v1/files/{file_key}/comments", query={"as_md": args.get("asMarkdown")})
    comments = payload.get("comments", []) if isinstance(payload, dict) else []
    return context.result_handle_store.build_items_result(source_tool_id="figma.comments_list", field_name="comments", items=comments, summary={"kind": "comments", "fileKey": file_key}, page_size=clamp_number(int(args.get("pageSize") or 50), 1, 200))


def _comment_create_body(args: dict[str, Any]) -> dict[str, Any]:
    body: dict[str, Any] = {"message": _require_string(args, "message")}
    if args.get("commentId"):
        body["comment_id"] = str(args["commentId"])
    if isinstance(args.get("clientMeta"), dict):
        body["client_meta"] = args["clientMeta"]
    return body


def _require_figma_token(context: AiToolExecutionContext) -> str:
    token = context.read_figma_api_token().strip()
    if not token:
        raise RuntimeError("Figma API token is required. Use figma.token_set, --figma-token, or FIGMA_TOKEN.")
    return token


def _require_string(args: dict[str, Any], key: str) -> str:
    value = str(args.get(key) or "").strip()
    if not value:
        raise RuntimeError(f"{key} is required.")
    return value


def _require_object(args: dict[str, Any], key: str) -> dict[str, Any]:
    value = args.get(key)
    if not isinstance(value, dict):
        raise RuntimeError(f"{key} must be an object.")
    return value


def _csv_required(value: Any, key: str) -> str:
    encoded = _csv_value(value)
    if not encoded:
        raise RuntimeError(f"{key} must contain at least one value.")
    return encoded


def _csv_value(value: Any) -> str:
    if isinstance(value, list):
        return ",".join(str(item).strip() for item in value if str(item).strip())
    if value is None:
        return ""
    return str(value).strip()
