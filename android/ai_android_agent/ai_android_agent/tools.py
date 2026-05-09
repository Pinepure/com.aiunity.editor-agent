from __future__ import annotations

from pathlib import Path
from typing import Any

from .android_runner import list_gradle_modules, parse_adb_devices, parse_package_list, probe_android_installation, resolve_existing_file, resolve_gradle_command, resolve_project_dir, run_command, start_background_process
from .models import AiManifestBundleDefinition, AiToolDanger, AiToolDefinition, AiToolExecutionContext, clamp_number, schema_json


def default_bundles() -> list[AiManifestBundleDefinition]:
    return [
        AiManifestBundleDefinition(id="service", description="Adapter health, logs, config, and recent calls.", prefixes=["service."]),
        AiManifestBundleDefinition(id="android.inspect", description="Android installation, project, and device inspection tools.", prefixes=["android.installation_", "android.project_", "android.devices_", "android.packages_", "android.avds_", "android.logcat_"]),
        AiManifestBundleDefinition(id="android.build", description="Android Gradle build and test tools.", prefixes=["android.gradle_"]),
        AiManifestBundleDefinition(id="android.mutate", description="Android emulator, install, and app launch tools.", prefixes=["android.emulator_start", "android.apk_install", "android.app_launch"]),
    ]


def register_default_tools(registry, context: AiToolExecutionContext) -> None:
    def register(**kwargs: Any) -> None:
        registry.register(AiToolDefinition(**kwargs))

    register(id="service.health_get", description="Return the adapter health payload.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.health_get", handler=lambda args, ctx: ctx.build_health_payload(include_ok=True))
    register(id="service.agent_brief_get", description="Return the concise operating brief for this adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.agent_brief_get", handler=lambda args, ctx: ctx.build_agent_brief_payload(include_ok=True))
    register(id="service.config_get", description="Return effective adapter configuration and default Android project state.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.config_get", handler=lambda args, ctx: {"rootDir": str(ctx.config.root_dir), "requireToken": ctx.config.require_token, "fullAccessEnabled": ctx.config.full_access_enabled, "acceptedTokenHeaders": ctx.config.accepted_token_headers, "adbExecutable": ctx.config.adb_executable, "gradleExecutable": ctx.config.gradle_executable, "emulatorExecutable": ctx.config.emulator_executable, "projectDir": _default_project_dir(ctx), "androidSdkRoot": ctx.config.android_sdk_root or None})
    register(id="service.logs_get", description="List recent service logs.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.logs_get", handler=lambda args, ctx: ctx.result_handle_store.build_items_result(source_tool_id="service.logs_get", field_name="logs", items=ctx.runtime_state.recent_logs(int(args.get("maxEntries") or 100)), summary={"kind": "logs"}, page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200)))
    register(id="service.recent_tool_calls_get", description="List recent tool call outcomes recorded by the adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.recent_tool_calls_get", handler=lambda args, ctx: ctx.result_handle_store.build_items_result(source_tool_id="service.recent_tool_calls_get", field_name="calls", items=ctx.runtime_state.recent_calls(int(args.get("maxEntries") or 100)), summary={"kind": "toolCalls"}, page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200)))
    register(id="service.token_regenerate", description="Regenerate the adapter token and return the new token value.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.token_regenerate", danger=AiToolDanger.MEDIUM, requires_confirmation=True, handler=lambda args, ctx: {"token": ctx.regenerate_token(), "acceptedTokenHeaders": ctx.config.accepted_token_headers})
    register(id="android.installation_get", description="Probe the configured Android SDK tooling and current project defaults.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="android.installation_get", handler=lambda args, ctx: probe_android_installation(ctx.config))
    register(id="android.project_summary_get", description="Return summary information for the configured Android Gradle project.", args_schema_json=schema_json(_project_dir_args_schema()), return_schema_json=schema_json({"type": "object"}), handler_name="android.project_summary_get", handler=lambda args, ctx: _project_summary(ctx, args))
    register(id="android.devices_list", description="List ADB-visible devices and emulators.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="android.devices_list", handler=lambda args, ctx: _devices_list(ctx))
    register(id="android.avds_list", description="List local Android Virtual Devices discovered by the emulator CLI.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="android.avds_list", handler=lambda args, ctx: _avds_list(ctx))
    register(id="android.packages_list", description="List installed packages on one device.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"serial": {"type": "string"}, "contains": {"type": "string"}, "pageSize": {"type": "integer", "minimum": 1, "maximum": 200}}}), return_schema_json=schema_json({"type": "object"}), handler_name="android.packages_list", handler=lambda args, ctx: _packages_list(ctx, args))
    register(id="android.logcat_recent_get", description="Return recent logcat output from one device.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"serial": {"type": "string"}, "lines": {"type": "integer", "minimum": 1, "maximum": 5000}, "format": {"type": "string"}, "length": {"type": "integer", "minimum": 1, "maximum": 32768}}}), return_schema_json=schema_json({"type": "object"}), handler_name="android.logcat_recent_get", handler=lambda args, ctx: _logcat_recent(ctx, args))
    register(id="android.gradle_tasks_list", description="Return the output of Gradle tasks --all for one Android project.", args_schema_json=schema_json(_project_dir_page_schema()), return_schema_json=schema_json({"type": "object"}), handler_name="android.gradle_tasks_list", handler=lambda args, ctx: _gradle_tasks(ctx, args))
    register(id="android.gradle_build", description="Run one or more Gradle build tasks in an Android project.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"projectDir": {"type": "string"}, "tasks": {"type": "array", "items": {"type": "string"}}, "extraArgs": {"type": "array", "items": {"type": "string"}}, "length": {"type": "integer", "minimum": 1, "maximum": 32768}}}), return_schema_json=schema_json({"type": "object"}), handler_name="android.gradle_build", danger=AiToolDanger.MEDIUM, handler=lambda args, ctx: _gradle_command_result(ctx, args, default_tasks=["assembleDebug"], source_tool_id="android.gradle_build"))
    register(id="android.gradle_test", description="Run one or more Gradle test tasks in an Android project.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"projectDir": {"type": "string"}, "tasks": {"type": "array", "items": {"type": "string"}}, "extraArgs": {"type": "array", "items": {"type": "string"}}, "length": {"type": "integer", "minimum": 1, "maximum": 32768}}}), return_schema_json=schema_json({"type": "object"}), handler_name="android.gradle_test", danger=AiToolDanger.MEDIUM, handler=lambda args, ctx: _gradle_command_result(ctx, args, default_tasks=["test"], source_tool_id="android.gradle_test"))
    register(id="android.emulator_start", description="Start one Android emulator instance by AVD name.", args_schema_json=schema_json({"type": "object", "required": ["avdName"], "additionalProperties": False, "properties": {"avdName": {"type": "string"}, "additionalArgs": {"type": "array", "items": {"type": "string"}}}}), return_schema_json=schema_json({"type": "object"}), handler_name="android.emulator_start", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _emulator_start(ctx, args))
    register(id="android.apk_install", description="Install one APK on a connected device with adb install.", args_schema_json=schema_json({"type": "object", "required": ["apkPath"], "additionalProperties": False, "properties": {"serial": {"type": "string"}, "apkPath": {"type": "string"}, "reinstall": {"type": "boolean"}, "grantAllRuntimePermissions": {"type": "boolean"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="android.apk_install", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _apk_install(ctx, args))
    register(id="android.app_launch", description="Launch one app on a device by package name and optional activity.", args_schema_json=schema_json({"type": "object", "required": ["packageName"], "additionalProperties": False, "properties": {"serial": {"type": "string"}, "packageName": {"type": "string"}, "activity": {"type": "string"}, "stopFirst": {"type": "boolean"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="android.app_launch", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _app_launch(ctx, args))


def _default_project_dir(context: AiToolExecutionContext) -> str | None:
    value = context.config.project_dir.strip()
    return str(resolve_project_dir(context.config.root_dir, value)) if value else None


def _project_dir_args_schema() -> dict[str, Any]:
    return {"type": "object", "additionalProperties": False, "properties": {"projectDir": {"type": "string"}}}


def _project_dir_page_schema() -> dict[str, Any]:
    schema = _project_dir_args_schema()
    schema["properties"]["length"] = {"type": "integer", "minimum": 1, "maximum": 32768}
    return schema


def _resolve_project_dir(context: AiToolExecutionContext, args: dict[str, Any]) -> Path:
    value = str(args.get("projectDir") or context.config.project_dir or "").strip()
    if not value:
        raise RuntimeError("This tool requires projectDir or a configured default project directory.")
    return resolve_project_dir(context.config.root_dir, value)


def _adb_base(context: AiToolExecutionContext, serial: str = "") -> list[str]:
    command = [context.config.adb_executable]
    serial = serial.strip()
    if serial:
        command.extend(["-s", serial])
    return command


def _project_summary(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    project_dir = _resolve_project_dir(context, args)
    modules = list_gradle_modules(project_dir)
    return {
        "projectDir": str(project_dir),
        "gradleWrapperPresent": (project_dir / "gradlew").exists(),
        "settingsFiles": [name for name in ("settings.gradle", "settings.gradle.kts") if (project_dir / name).exists()],
        "modules": modules,
        "appModulePresent": ":app" in modules or (project_dir / "app").exists(),
    }


def _devices_list(context: AiToolExecutionContext) -> dict[str, Any]:
    result = run_command([context.config.adb_executable, "devices", "-l"], timeout_seconds=context.config.tool_timeout_seconds)
    devices = parse_adb_devices(result["stdout"])
    return context.result_handle_store.build_items_result(source_tool_id="android.devices_list", field_name="devices", items=devices, summary={"kind": "adbDevices"}, page_size=100)


def _avds_list(context: AiToolExecutionContext) -> dict[str, Any]:
    command = [probe_android_installation(context.config)["configuredEmulatorExecutable"], "-list-avds"]
    result = run_command(command, timeout_seconds=context.config.tool_timeout_seconds)
    avds = [line.strip() for line in result["stdout"].splitlines() if line.strip()]
    return context.result_handle_store.build_items_result(source_tool_id="android.avds_list", field_name="avds", items=avds, summary={"kind": "avds"}, page_size=100)


def _packages_list(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    serial = str(args.get("serial") or "")
    contains = str(args.get("contains") or "")
    command = _adb_base(context, serial) + ["shell", "pm", "list", "packages"]
    result = run_command(command, timeout_seconds=context.config.tool_timeout_seconds)
    packages = parse_package_list(result["stdout"], contains=contains)
    return context.result_handle_store.build_items_result(source_tool_id="android.packages_list", field_name="packages", items=packages, summary={"kind": "packages", "serial": serial or None, "contains": contains or None}, page_size=clamp_number(int(args.get("pageSize") or 100), 1, 200))


def _logcat_recent(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    serial = str(args.get("serial") or "")
    lines = clamp_number(int(args.get("lines") or 200), 1, 5000)
    fmt = str(args.get("format") or "threadtime")
    command = _adb_base(context, serial) + ["logcat", "-d", "-v", fmt, "-t", str(lines)]
    result = run_command(command, timeout_seconds=context.config.tool_timeout_seconds)
    return context.result_handle_store.build_text_result(source_tool_id="android.logcat_recent_get", text=result["stdout"], summary={"kind": "logcat", "serial": serial or None, "lines": lines}, length=clamp_number(int(args.get("length") or 4096), 1, 32768))


def _gradle_tasks(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    project_dir = _resolve_project_dir(context, args)
    command = resolve_gradle_command(context.config, project_dir=project_dir) + ["tasks", "--all"]
    result = run_command(command, timeout_seconds=context.config.tool_timeout_seconds, cwd=project_dir)
    return context.result_handle_store.build_text_result(source_tool_id="android.gradle_tasks_list", text=result["stdout"], summary={"kind": "gradleTasks", "projectDir": str(project_dir)}, length=clamp_number(int(args.get("length") or 4096), 1, 32768))


def _gradle_command_result(context: AiToolExecutionContext, args: dict[str, Any], *, default_tasks: list[str], source_tool_id: str) -> dict[str, Any]:
    project_dir = _resolve_project_dir(context, args)
    tasks = [str(item) for item in args.get("tasks", []) if str(item).strip()] if isinstance(args.get("tasks"), list) else []
    extra_args = [str(item) for item in args.get("extraArgs", []) if str(item).strip()] if isinstance(args.get("extraArgs"), list) else []
    command = resolve_gradle_command(context.config, project_dir=project_dir) + (tasks or default_tasks) + extra_args
    result = run_command(command, timeout_seconds=context.config.tool_timeout_seconds, cwd=project_dir)
    text = "\n".join(part for part in [result["stdout"], result["stderr"]] if part)
    return context.result_handle_store.build_text_result(source_tool_id=source_tool_id, text=text, summary={"kind": "gradleCommand", "projectDir": str(project_dir), "command": command}, length=clamp_number(int(args.get("length") or 4096), 1, 32768))


def _emulator_start(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    avd_name = str(args.get("avdName") or "").strip()
    if not avd_name:
        raise RuntimeError("avdName is required.")
    additional_args = [str(item) for item in args.get("additionalArgs", []) if str(item).strip()] if isinstance(args.get("additionalArgs"), list) else []
    configured = probe_android_installation(context.config)["configuredEmulatorExecutable"]
    return start_background_process([configured, "-avd", avd_name] + additional_args)


def _apk_install(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    serial = str(args.get("serial") or "")
    apk_path = resolve_existing_file(context.config.root_dir, str(args.get("apkPath") or ""))
    command = _adb_base(context, serial) + ["install"]
    if bool(args.get("reinstall", True)):
        command.append("-r")
    if bool(args.get("grantAllRuntimePermissions", False)):
        command.append("-g")
    command.append(str(apk_path))
    return run_command(command, timeout_seconds=context.config.tool_timeout_seconds)


def _app_launch(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    serial = str(args.get("serial") or "")
    package_name = str(args.get("packageName") or "").strip()
    activity = str(args.get("activity") or "").strip()
    if not package_name:
        raise RuntimeError("packageName is required.")
    outputs: list[dict[str, Any]] = []
    if bool(args.get("stopFirst", False)):
        outputs.append(run_command(_adb_base(context, serial) + ["shell", "am", "force-stop", package_name], timeout_seconds=context.config.tool_timeout_seconds, check=False))
    if activity:
        outputs.append(run_command(_adb_base(context, serial) + ["shell", "am", "start", "-n", f"{package_name}/{activity}"], timeout_seconds=context.config.tool_timeout_seconds))
    else:
        outputs.append(run_command(_adb_base(context, serial) + ["shell", "monkey", "-p", package_name, "-c", "android.intent.category.LAUNCHER", "1"], timeout_seconds=context.config.tool_timeout_seconds))
    return {"serial": serial or None, "packageName": package_name, "activity": activity or None, "steps": outputs}
