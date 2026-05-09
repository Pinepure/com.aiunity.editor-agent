from __future__ import annotations

from pathlib import Path
from typing import Any

from .models import AiManifestBundleDefinition, AiToolDanger, AiToolDefinition, AiToolExecutionContext, clamp_number, schema_json
from .wpf_runner import list_files, list_resource_keys, list_solution_projects, probe_wpf_installation, resolve_project_dir, resolve_solution_path, run_command, search_text, set_xaml_attribute


def default_bundles() -> list[AiManifestBundleDefinition]:
    return [
        AiManifestBundleDefinition(id="service", description="Adapter health, logs, config, and recent calls.", prefixes=["service."]),
        AiManifestBundleDefinition(id="wpf.inspect", description="WPF installation, solution, project, and XAML inspection tools.", prefixes=["wpf.installation_", "wpf.solution_", "wpf.projects_", "wpf.xaml_", "wpf.xamls_", "wpf.resources_"]),
        AiManifestBundleDefinition(id="wpf.build", description="WPF build and test tools.", prefixes=["wpf.dotnet_", "wpf.msbuild_"]),
        AiManifestBundleDefinition(id="wpf.mutate", description="Controlled XAML mutation tools.", prefixes=["wpf.xaml_attribute_set"]),
    ]


def register_default_tools(registry, context: AiToolExecutionContext) -> None:
    def register(**kwargs: Any) -> None:
        registry.register(AiToolDefinition(**kwargs))

    register(id="service.health_get", description="Return the adapter health payload.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.health_get", handler=lambda args, ctx: ctx.build_health_payload(include_ok=True))
    register(id="service.agent_brief_get", description="Return the concise operating brief for this adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.agent_brief_get", handler=lambda args, ctx: ctx.build_agent_brief_payload(include_ok=True))
    register(id="service.config_get", description="Return effective adapter configuration and default workspace state.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.config_get", handler=lambda args, ctx: {"rootDir": str(ctx.config.root_dir), "requireToken": ctx.config.require_token, "fullAccessEnabled": ctx.config.full_access_enabled, "acceptedTokenHeaders": ctx.config.accepted_token_headers, "dotnetExecutable": ctx.config.dotnet_executable, "msbuildExecutable": ctx.config.msbuild_executable, "solutionPath": ctx.config.solution_path or None, "projectDir": ctx.config.project_dir or None})
    register(id="service.logs_get", description="List recent service logs.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.logs_get", handler=lambda args, ctx: ctx.result_handle_store.build_items_result(source_tool_id="service.logs_get", field_name="logs", items=ctx.runtime_state.recent_logs(int(args.get("maxEntries") or 100)), summary={"kind": "logs"}, page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200)))
    register(id="service.recent_tool_calls_get", description="List recent tool call outcomes recorded by the adapter.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"maxEntries": {"type": "integer", "minimum": 1, "maximum": 300}}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.recent_tool_calls_get", handler=lambda args, ctx: ctx.result_handle_store.build_items_result(source_tool_id="service.recent_tool_calls_get", field_name="calls", items=ctx.runtime_state.recent_calls(int(args.get("maxEntries") or 100)), summary={"kind": "toolCalls"}, page_size=clamp_number(int(args.get("maxEntries") or 100), 1, 200)))
    register(id="service.token_regenerate", description="Regenerate the adapter token and return the new token value.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="service.token_regenerate", danger=AiToolDanger.MEDIUM, requires_confirmation=True, handler=lambda args, ctx: {"token": ctx.regenerate_token(), "acceptedTokenHeaders": ctx.config.accepted_token_headers})
    register(id="wpf.installation_get", description="Probe dotnet, msbuild, and configured WPF workspace paths.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {}}), return_schema_json=schema_json({"type": "object"}), handler_name="wpf.installation_get", handler=lambda args, ctx: probe_wpf_installation(ctx.config))
    register(id="wpf.solution_summary_get", description="Return summary information for the configured WPF solution or project.", args_schema_json=schema_json(_workspace_args_schema()), return_schema_json=schema_json({"type": "object"}), handler_name="wpf.solution_summary_get", handler=lambda args, ctx: _solution_summary(ctx, args))
    register(id="wpf.projects_list", description="List projects referenced by a WPF solution.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"solutionPath": {"type": "string"}, "pageSize": {"type": "integer", "minimum": 1, "maximum": 200}}}), return_schema_json=schema_json({"type": "object"}), handler_name="wpf.projects_list", handler=lambda args, ctx: _projects_list(ctx, args))
    register(id="wpf.xamls_list", description="List XAML files under the WPF workspace.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"solutionPath": {"type": "string"}, "projectDir": {"type": "string"}, "pageSize": {"type": "integer", "minimum": 1, "maximum": 200}}}), return_schema_json=schema_json({"type": "object"}), handler_name="wpf.xamls_list", handler=lambda args, ctx: _xamls_list(ctx, args))
    register(id="wpf.xamls_search", description="Search text across XAML and C# files in the workspace.", args_schema_json=schema_json({"type": "object", "required": ["query"], "additionalProperties": False, "properties": {"solutionPath": {"type": "string"}, "projectDir": {"type": "string"}, "query": {"type": "string"}, "maxResults": {"type": "integer", "minimum": 1, "maximum": 1000}, "pageSize": {"type": "integer", "minimum": 1, "maximum": 200}}}), return_schema_json=schema_json({"type": "object"}), handler_name="wpf.xamls_search", handler=lambda args, ctx: _xamls_search(ctx, args))
    register(id="wpf.xaml_get", description="Read one XAML file from the workspace.", args_schema_json=schema_json({"type": "object", "required": ["xamlPath"], "additionalProperties": False, "properties": {"xamlPath": {"type": "string"}, "length": {"type": "integer", "minimum": 1, "maximum": 32768}}}), return_schema_json=schema_json({"type": "object"}), handler_name="wpf.xaml_get", handler=lambda args, ctx: _xaml_get(ctx, args))
    register(id="wpf.resources_list", description="List XAML resource keys found in one file.", args_schema_json=schema_json({"type": "object", "required": ["xamlPath"], "additionalProperties": False, "properties": {"xamlPath": {"type": "string"}, "pageSize": {"type": "integer", "minimum": 1, "maximum": 200}}}), return_schema_json=schema_json({"type": "object"}), handler_name="wpf.resources_list", handler=lambda args, ctx: _resources_list(ctx, args))
    register(id="wpf.dotnet_build", description="Run dotnet build for the configured WPF solution or project.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"solutionPath": {"type": "string"}, "projectDir": {"type": "string"}, "configuration": {"type": "string"}, "extraArgs": {"type": "array", "items": {"type": "string"}}, "length": {"type": "integer", "minimum": 1, "maximum": 32768}}}), return_schema_json=schema_json({"type": "object"}), handler_name="wpf.dotnet_build", danger=AiToolDanger.MEDIUM, handler=lambda args, ctx: _run_build_command(ctx, args, source_tool_id="wpf.dotnet_build", use_msbuild=False))
    register(id="wpf.dotnet_test", description="Run dotnet test for the configured WPF solution or project.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"solutionPath": {"type": "string"}, "projectDir": {"type": "string"}, "configuration": {"type": "string"}, "extraArgs": {"type": "array", "items": {"type": "string"}}, "length": {"type": "integer", "minimum": 1, "maximum": 32768}}}), return_schema_json=schema_json({"type": "object"}), handler_name="wpf.dotnet_test", danger=AiToolDanger.MEDIUM, handler=lambda args, ctx: _run_test_command(ctx, args))
    register(id="wpf.msbuild_build", description="Run msbuild for the configured WPF solution or project.", args_schema_json=schema_json({"type": "object", "additionalProperties": False, "properties": {"solutionPath": {"type": "string"}, "projectDir": {"type": "string"}, "configuration": {"type": "string"}, "extraArgs": {"type": "array", "items": {"type": "string"}}, "length": {"type": "integer", "minimum": 1, "maximum": 32768}}}), return_schema_json=schema_json({"type": "object"}), handler_name="wpf.msbuild_build", danger=AiToolDanger.MEDIUM, handler=lambda args, ctx: _run_build_command(ctx, args, source_tool_id="wpf.msbuild_build", use_msbuild=True))
    register(id="wpf.xaml_attribute_set", description="Set one attribute on a XAML element identified by Name or x:Name.", args_schema_json=schema_json({"type": "object", "required": ["xamlPath", "elementName", "attributeName", "value"], "additionalProperties": False, "properties": {"xamlPath": {"type": "string"}, "elementName": {"type": "string"}, "attributeName": {"type": "string"}, "value": {"type": "string"}, "outputPath": {"type": "string"}, "inPlace": {"type": "boolean"}}}), return_schema_json=schema_json({"type": "object"}), handler_name="wpf.xaml_attribute_set", danger=AiToolDanger.HIGH, requires_confirmation=True, handler=lambda args, ctx: _xaml_attribute_set(ctx, args))


def _workspace_args_schema() -> dict[str, Any]:
    return {"type": "object", "additionalProperties": False, "properties": {"solutionPath": {"type": "string"}, "projectDir": {"type": "string"}}}


def _resolve_solution(context: AiToolExecutionContext, args: dict[str, Any], *, required: bool = False) -> Path | None:
    value = str(args.get("solutionPath") or context.config.solution_path or "").strip()
    if not value:
        if required:
            raise RuntimeError("solutionPath is required.")
        return None
    return resolve_solution_path(context.config.root_dir, value)


def _resolve_workspace_root(context: AiToolExecutionContext, args: dict[str, Any]) -> Path:
    if str(args.get("projectDir") or context.config.project_dir or "").strip():
        return resolve_project_dir(context.config.root_dir, str(args.get("projectDir") or context.config.project_dir))
    solution = _resolve_solution(context, args, required=False)
    if solution is not None:
        return solution.parent
    raise RuntimeError("Provide projectDir or solutionPath, or configure one of them for the adapter.")


def _resolve_xaml_path(context: AiToolExecutionContext, value: str) -> Path:
    path = Path(value).expanduser()
    resolved = path.resolve() if path.is_absolute() else (context.config.root_dir / path).resolve()
    if not resolved.exists():
        raise FileNotFoundError(f"XAML file does not exist: {resolved}")
    return resolved


def _solution_summary(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    workspace_root = _resolve_workspace_root(context, args)
    solution = _resolve_solution(context, args, required=False)
    projects = list_solution_projects(solution) if solution is not None else []
    xaml_files = list_files(workspace_root, suffixes=(".xaml",))
    return {"workspaceRoot": str(workspace_root), "solutionPath": str(solution) if solution else None, "projectCount": len(projects), "projects": projects, "xamlCount": len(xaml_files)}


def _projects_list(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    solution = _resolve_solution(context, args, required=True)
    projects = list_solution_projects(solution)
    return context.result_handle_store.build_items_result(source_tool_id="wpf.projects_list", field_name="projects", items=projects, summary={"kind": "solutionProjects", "solutionPath": str(solution)}, page_size=clamp_number(int(args.get("pageSize") or 50), 1, 200))


def _xamls_list(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    workspace_root = _resolve_workspace_root(context, args)
    items = [{"path": str(path), "relativePath": str(path.relative_to(workspace_root))} for path in list_files(workspace_root, suffixes=(".xaml",))]
    return context.result_handle_store.build_items_result(source_tool_id="wpf.xamls_list", field_name="xamls", items=items, summary={"kind": "xamlFiles", "workspaceRoot": str(workspace_root)}, page_size=clamp_number(int(args.get("pageSize") or 100), 1, 200))


def _xamls_search(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    workspace_root = _resolve_workspace_root(context, args)
    query = str(args.get("query") or "").strip()
    if not query:
        raise RuntimeError("query is required.")
    matches = search_text(workspace_root, query=query, suffixes=(".xaml", ".cs"), max_results=clamp_number(int(args.get("maxResults") or 200), 1, 1000))
    return context.result_handle_store.build_items_result(source_tool_id="wpf.xamls_search", field_name="matches", items=matches, summary={"kind": "searchMatches", "workspaceRoot": str(workspace_root), "query": query}, page_size=clamp_number(int(args.get("pageSize") or 100), 1, 200))


def _xaml_get(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    xaml_path = _resolve_xaml_path(context, str(args.get("xamlPath") or ""))
    text = xaml_path.read_text(encoding="utf-8", errors="replace")
    return context.result_handle_store.build_text_result(source_tool_id="wpf.xaml_get", text=text, summary={"kind": "xamlFile", "xamlPath": str(xaml_path)}, length=clamp_number(int(args.get("length") or 4096), 1, 32768))


def _resources_list(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    xaml_path = _resolve_xaml_path(context, str(args.get("xamlPath") or ""))
    keys = list_resource_keys(xaml_path)
    return context.result_handle_store.build_items_result(source_tool_id="wpf.resources_list", field_name="resourceKeys", items=keys, summary={"kind": "resourceKeys", "xamlPath": str(xaml_path)}, page_size=clamp_number(int(args.get("pageSize") or 100), 1, 200))


def _run_build_command(context: AiToolExecutionContext, args: dict[str, Any], *, source_tool_id: str, use_msbuild: bool) -> dict[str, Any]:
    workspace_root = _resolve_workspace_root(context, args)
    target = str(_resolve_solution(context, args, required=False) or workspace_root)
    configuration = str(args.get("configuration") or "Debug")
    extra_args = [str(item) for item in args.get("extraArgs", []) if str(item).strip()] if isinstance(args.get("extraArgs"), list) else []
    if use_msbuild:
        command = [context.config.msbuild_executable, target, f"/p:Configuration={configuration}"] + extra_args
    else:
        command = [context.config.dotnet_executable, "build", target, "-c", configuration] + extra_args
    result = run_command(command, timeout_seconds=context.config.tool_timeout_seconds, cwd=workspace_root)
    return context.result_handle_store.build_text_result(source_tool_id=source_tool_id, text="\n".join(part for part in [result["stdout"], result["stderr"]] if part), summary={"kind": "buildOutput", "command": command}, length=clamp_number(int(args.get("length") or 4096), 1, 32768))


def _run_test_command(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    workspace_root = _resolve_workspace_root(context, args)
    target = str(_resolve_solution(context, args, required=False) or workspace_root)
    configuration = str(args.get("configuration") or "Debug")
    extra_args = [str(item) for item in args.get("extraArgs", []) if str(item).strip()] if isinstance(args.get("extraArgs"), list) else []
    command = [context.config.dotnet_executable, "test", target, "-c", configuration] + extra_args
    result = run_command(command, timeout_seconds=context.config.tool_timeout_seconds, cwd=workspace_root)
    return context.result_handle_store.build_text_result(source_tool_id="wpf.dotnet_test", text="\n".join(part for part in [result["stdout"], result["stderr"]] if part), summary={"kind": "testOutput", "command": command}, length=clamp_number(int(args.get("length") or 4096), 1, 32768))


def _xaml_attribute_set(context: AiToolExecutionContext, args: dict[str, Any]) -> dict[str, Any]:
    xaml_path = _resolve_xaml_path(context, str(args.get("xamlPath") or ""))
    output_value = str(args.get("outputPath") or "").strip()
    in_place = bool(args.get("inPlace", False))
    if in_place:
        output_path = xaml_path
    elif output_value:
        output_path = _resolve_xaml_path_parent(context, output_value)
    else:
        output_path = xaml_path.with_name(f"{xaml_path.stem}.updated{xaml_path.suffix}")
    return set_xaml_attribute(xaml_path=xaml_path, element_name=str(args.get("elementName") or ""), attribute_name=str(args.get("attributeName") or ""), value=str(args.get("value") or ""), output_path=output_path)


def _resolve_xaml_path_parent(context: AiToolExecutionContext, value: str) -> Path:
    path = Path(value).expanduser()
    resolved = path.resolve() if path.is_absolute() else (context.config.root_dir / path).resolve()
    resolved.parent.mkdir(parents=True, exist_ok=True)
    return resolved
