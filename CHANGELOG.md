# Changelog

## 0.3.0

- Added workflow recipe storage, workset persistence, batch execution, endpoint registry, and snapshot/state hashing utilities for faster repeatable task execution.
- Added validation, context, git, workflow, batch, and workset tool groups so agents can plan, execute, and verify Unity changes with less token overhead.
- Added dynamic endpoint persistence and local state files for token, port, recipes, worksets, snapshots, and validation artifacts under the Unity agent library path.
- Updated service guidance and runtime behavior to support stronger validation-first and automation-first Unity editing flows.

## 0.2.0

- Added protocol-level optimized discovery with `manifestHash`, lightweight `/manifest`, `/manifest/full`, `/manifest/search`, `/manifest/bundles`, `/tool/describe_many`, and `/agent/brief`.
- Added manifest summary, manifest bundle, manifest search, and tool description helper tools so AI agents can discover capabilities without repeatedly loading the full schema set.
- Added paged `resultHandle` support for large tool responses and updated high-volume tools to return first-page summaries instead of full payloads by default.
- Added `compile.snapshot` and `compile.errors_summary` for lower-token diagnostics while preserving accurate follow-up drill-down.
- Updated Control Center guidance and protocol documentation to the new cache-aware discovery and paging workflow.

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
