# AI JetBrains Agent

AI JetBrains Agent is the JetBrains IDE adapter for the shared AI Platform Agent Framework.

It runs as an IntelliJ Platform plugin and exposes the same discovery-first protocol used by the other adapters.

## First-phase coverage

- open projects and modules
- workspace file enumeration
- document readback
- PSI tree inspection
- run configuration inspection
- generated tool registration, reload, and execution through plugin-hosted Groovy scripts

## Host model

This adapter is built as an IntelliJ Platform plugin and uses official project model, VFS, PSI, and run configuration APIs.

## Dynamic generated tools

- `service.config_get` and `GET /health` advertise `supportsDynamicToolRegistration: true`.
- Generated tool definitions live under `.ai_platform_agent/jetbrains/generated_tools/`.
- Built-in management tools are `tool.get_template`, `tool.list_generated`, `tool.upsert_generated`, `tool.delete_generated`, and `tool.reload_generated`.
- Generated source executes as Groovy with bindings `args`, `host`, and `json`.

## Minimal integration

For a standalone JetBrains product, ship only this IntelliJ Platform plugin project.

- keep `jetbrains/ai_jetbrains_agent/`
- do not bundle VS Code, Unity, Photoshop, or other platform adapters
- let the JetBrains plugin own PSI, VFS, run configurations, and generated-tool behavior
- let the AI client talk only to the local HTTP service exposed by this plugin
