from __future__ import annotations

from pathlib import Path
from typing import Any

from .ios_runner import build_xcode_context_args, probe_ios_installation, resolve_optional_path, run_command, simctl_json
from .models import AiManifestBundleDefinition, AiToolDanger, AiToolDefinition, AiToolExecutionContext, clamp_number, schema_json


def default_bundles() -> list[AiManifestBundleDefinition]:
    return [
        AiManifestBundleDefinition(id="service", description="Adapter health, logs, config, and recent calls.", prefixes=["service."]),
        AiManifestBundleDefinition(id="ios.inspect", description="iOS installation, scheme, simulator, and log inspection tools.", prefixes=["ios.installation_", "ios.project_", "ios.schemes_", "ios.simulators_", "ios.logs_"]),
        AiManifestBundleDefinition(id="ios.build", description="iOS build and test tools.", prefixes=["ios.build", "ios.test"]),
        AiManifestBundleDefinition(id="ios.mutate", description="Simulator and app mutation tools.", prefixes=["ios.simulator_", "ios.app_"]),
    ]


def register_default_tools(registry, context: AiToolExecutionContext) -> None:
    def register(**kwargs: Any) -> None:
        registry.register(AiToolDefinition(**kwargs))

    register(id="service.health_get", description="Return the adapter health payload.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.health_get", handler=lambda args, ctx: ctx.build_health_payload(include_ok=True))
    register(id="service.agent_brief_get", description="Return the concise operating brief for this adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.agent_brief_get", handler=lambda args, ctx: ctx.build_agent_brief_payload(include_ok=True))
    register(id="service.config_get", description="Return effective adapter configuration and Xcode defaults.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.config_get", handler=lambda args, ctx: {"rootDir": str(ctx.config.root_dir), "requireToken": ctx.config.require_token, "fullAccessEnabled": ctx.config.full_access_enabled, "acceptedTokenHeaders": ctx.config.accepted_token_headers, "xcodebuildExecutable": ctx.config.xcodebuild_executable, "xcrunExecutable": ctx.config.xcrun_executable, "projectPath": ctx.config.project_path or None, "workspacePath": ctx.config.workspace_path or None, "scheme": ctx.config.scheme or None, "destination": ctx.config.destination})
    register(id="service.logs_get", description="List recent service logs.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.logs_get", handler=lambda args, ctx: ctx.result_handle_store.build_items_result(source_tool_id="service.logs_get", field_name="logs", items=ctx.runtime_state.recent_logs(int(args.get("maxEntries") or 100)), summary={"kind": "logs"}, page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200)))
    register(id="service.recent_tool_calls_get", description="List recent tool call outcomes recorded by the adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.recent_tool_calls_get", handler=lambda args, ctx: ctx.result_handle_store.build_items_result(source_tool_id="service.recent_tool_calls_get", field_name="calls", items=ctx.runtime_state.recent_calls(int(args.get("maxEntries") or 100)), summary={"kind": "toolCalls"}, page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200)))
    register(id="service.token_regenerate", description="Regenerate the adapter token and return the new token value.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.token_regenerate", danger=AiToolDanger.MEDIUM, requires_confirmation=True, handler=lambda args, ctx: {"token": ctx.regenerate_token(), "acceptedTokenHeaders": ctx.config.accepted_token_headers})
    register(id="ios.installation_get", description="Probe Xcode and simctl tooling plus configured project settings.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="ios.installation_get", handler=lambda args, ctx: probe_ios_installation(ctx.config))
    register(id="ios.project_summary_get", description="Return summary information for the configured Xcode project or workspace.", args_schema_json=schema_json(_project_args_schema()), return_schema_json=schema_json({"type": "object"}), handler_name="ios.project_summary_get", handler=lambda args, ctx: _project_summary(ctx, args))
    register(id="ios.schemes_list", description="List schemes visible to xcodebuild for the configured project or workspace.", args_schema_json=schema_json(_project_args_schema()), return_schema_json=schema_json({"type": "object"}), handler_name="ios.schemes_list", handler=lambda args, ctx: _schemes_list(ctx, args))
    register(id="ios.simulators_list", description="List available iOS simulators from simctl.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"pageSize": {"type": "integer", "minimum": 1, "maximum": 200}}}), return_schema_json=schema_json({"type": "object"}), handler_name="ios.simulators_list", handler=lambda args, ctx: _simulators_list(ctx, args))
    register(id="ios.logs_recent_get", description="Return recent unified logs from a simulator.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"udid": {"type": "string"}, "last": {"type": "string"}, "length": {"type": "integer", "minimum": 1, "maximum": 32768}}}), return_schema_json=schema_json({"type": "object"}), handler_name="ios.logs_recent_get", handler=lambda args, ctx: _logs_recent(ctx, args))
    register(id="ios.build", description="Run xcodebuild build for the configured project or workspace.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"projectPath": {"type": "string"}, "workspacePath": {"type": "string"}, "scheme": {"type": "string"}, "destination": {"type": "string"}, "configuration": {"type": "string"}, "extraArgs": {"type": "array", "items": {"type": "string"}}, "length": {"type": "integer", "minimum": 1, "maximum": 32768}}}), return_schema_json=schema_json({"type": "object"}), handler_name="ios.build", danger=AiToolDanger.MEDIUM, handler=lambda args, ctx: _xcodebuild_command(ctx, args, action="build", source_tool_id="ios.build"))
    register(id="ios.test", description="Run xcodebuild test for the configured project or workspace.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"projectPath": {"type": "string"}, "workspacePath": {"type": "string"}, "scheme": {"type": "string"}, "destination": {"type": "string"}, "configuration": {"type": "string"}, "extraArgs": {"type": "array", "items": {"type": "string"}}, "length": {"type": "integer", "minimum": 1, "maximum": 32768}}}), return_schema_json=schema_json({"type": "object"}), handler_name="ios.test", danger=AiToolDanger.MEDIUM, handler=lambda args, ctx: _xcodebuild_command(ctx, args, action="test", source_tool_id="ios.test"))
    register(id="ios.simulator_boot", description="Boot one iOS simulator by UDID.", args_schema_json=schema_json({"type": "object", "required": ["udid"], "additionalProperties": False, "properties": {"udid": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="ios.simulator_boot", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: run_command([ctx.config.xcrun_executable, "simctl", "boot", str(args.get("udid") or "")], timeout_seconds=ctx.config.tool_timeout_seconds))
    register(id="ios.simulator_shutdown", description="Shutdown one iOS simulator by UDID.", args_schema_json=schema_json({"type": "object", "required": ["udid"], "additionalProperties": False, "properties": {"udid": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="ios.simulator_shutdown", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: run_command([ctx.config.xcrun_executable, "simctl", "shutdown", str(args.get("udid") or "")], timeout_seconds=ctx.config.tool_timeout_seconds))
    register(id="ios.app_install", description="Install one .app bundle onto a simulator.", args_schema_json=schema_json({"type": "object", "required": ["appPath"], "additionalProperties": False, "properties": {"udid": {"type": "string"}, "appPath": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="ios.app_install", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _app_install(ctx, args))
    register(id="ios.app_launch", description="Launch one app on a simulator by bundle identifier.", args_schema_json=schema_json({"type": "object", "required": ["bundleId"], "additionalProperties": False, "properties": {"udid": {"type": "string"}, "bundleId": {"type": "string"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="ios.app_launch", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: run_command([ctx.config.xcrun_executable, "simctl", "launch", _udid_or_booted(args), str(args.get("bundleId") or "")], timeout_seconds=ctx.config.tool_timeout_seconds))


def _project_args_schema() -> dict[str, Any]:
    return {"type": "object", "additionalProperties": False, "properties": {"projectPath": {"type": "string"}, "workspacePath": {"type": "string"}, "scheme": {"type": "string"}}}


def _resolve_project(context: AiToolExecutionContext, args: dict[str, Any]) -> Path | None:
    value = str(args.get("projectPath") or context.config.project_path or "").strip()
    return resolve_optional_path(context.config.root_dir, value) if value else None


def _resolve_workspace(context: AiToolExecutionContext, args: dict[str, Any]) -> Path | None:
    value = str(args.get("workspacePath") or context.config.workspace_path or "").strip()
    return resolve_optional_path(context.config.root_dir, value) if value else None


def _scheme(context: AiToolExecutionContext, args: dict[str, Any]) -> str:
    return str(args.get("scheme") or context.config.scheme or "").strip()


def _destination(context: AiToolExecutionContext, args: dict[str, Any]) -> str:
    return str(args.get("destination") or context.config.destination or "").strip()


def _project_summary(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    project = _resolve_project(context, args)
    workspace = _resolve_workspace(context, args)
    scheme = _scheme(context, args)
    return {"projectPath": str(project) if project else None, "workspacePath": str(workspace) if workspace else None, "scheme": scheme or None, "destination": _destination(context, args)}


def _schemes_list(context: AiToolExecutionContext, args: dict[str, Any]) -> Any:
    project = _resolve_project(context, args)
    workspace = _resolve_workspace(context, args)
    command = [context.config.xcodebuild_executable, *build_xcode_context_args(project_path=project, workspace_path=workspace, scheme=""), "-list", "-json"]
    result = run_command(command, timeout_seconds=context.config.tool_timeout_seconds)
    return __import__("json").loads(result["stdout"])


def _simulators_list(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    payload = simctl_json(context.config, "list", "devices", "available", "--json")
    devices = []
    for runtime, runtime_devices in payload.get("devices", {}).items():
        for device in runtime_devices:
            devices.append({"runtime": runtime, **device})
    return context.result_handle_store.build_items_result(source_tool_id="ios.simulators_list", field_name="simulators", items=devices, summary={"kind": "simulators"}, page_size=clamp_number(int(args.get("pageSize") or 100), 1, 200))


def _logs_recent(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    last = str(args.get("last") or "10m")
    command = [context.config.xcrun_executable, "simctl", "spawn", _udid_or_booted(args), "log", "show", "--style", "compact", "--last", last]
    result = run_command(command, timeout_seconds=context.config.tool_timeout_seconds)
    return context.result_handle_store.build_text_result(source_tool_id="ios.logs_recent_get", text=result["stdout"], summary={"kind": "simulatorLogs", "udid": args.get("udid") or "booted", "last": last}, length=clamp_number(int(args.get("length") or 4096), 1, 32768))


def _xcodebuild_command(context: AiToolExecutionContext, args: dict[str, Any], *, action: str, source_tool_id: str) -> dict[str, Any]:
    project = _resolve_project(context, args)
    workspace = _resolve_workspace(context, args)
    scheme = _scheme(context, args)
    if not scheme:
        raise RuntimeError("scheme is required for xcodebuild commands.")
    destination = _destination(context, args)
    configuration = str(args.get("configuration") or "Debug")
    extra_args = [str(item) for item in args.get("extraArgs", []) if str(item).strip()] if isinstance(args.get("extraArgs"), list) else []
    command = [context.config.xcodebuild_executable, *build_xcode_context_args(project_path=project, workspace_path=workspace, scheme=scheme), "-configuration", configuration, "-destination", destination, action, *extra_args]
    cwd = workspace.parent if workspace is not None else (project.parent if project is not None else context.config.root_dir)
    result = run_command(command, timeout_seconds=context.config.tool_timeout_seconds, cwd=cwd)
    return context.result_handle_store.build_text_result(source_tool_id=source_tool_id, text="\n".join(part for part in [result["stdout"], result["stderr"]] if part), summary={"kind": "xcodebuild", "command": command}, length=clamp_number(int(args.get("length") or 4096), 1, 32768))


def _app_install(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    app_path = resolve_optional_path(context.config.root_dir, str(args.get("appPath") or ""))
    command = [context.config.xcrun_executable, "simctl", "install", _udid_or_booted(args), str(app_path)]
    return run_command(command, timeout_seconds=context.config.tool_timeout_seconds)


def _udid_or_booted(args: dict[str, Any]) -> str:
    value = str(args.get("udid") or "").strip()
    return value or "booted"
