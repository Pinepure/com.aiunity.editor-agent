# Changelog

## 0.1.1

- Added a full access mode toggle in the Control Center Settings view to skip all tool confirmation dialogs.
- Applied the full access execution policy consistently to both HTTP tool calls and Control Center manual tool execution.
- Exposed `fullAccessEnabled` in `service.config_get` so external agents can inspect the current confirmation policy.

## 0.1.0

- Initial Unity Editor-only package.
- Local HTTP API service with token protection.
- Auto-discovered `[AiTool]` registry and `/manifest` endpoint.
- Built-in compile diagnostics, asset search/dependency tools, prefab creation from JSON, generated tool management, scene and selection utilities.
- AGENT.md protocol guide.
- AI Editor Agent Control Center window.
