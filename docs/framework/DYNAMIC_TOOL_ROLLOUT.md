# Dynamic Tool Rollout

This document tracks which adapters already support self-extension and how the next batch should be implemented.

## Goal

Every dynamic adapter should let an AI agent do this loop without editing the adapter core package itself:

1. discover that the current manifest lacks a needed tool
2. request a safe template
3. write a generated tool definition into the platform-owned generated tools directory
4. reload the manifest
5. call the new tool
6. iterate on the generated tool if the first version is insufficient

## Shared contract

Dynamic adapters should expose:

- `tool.get_template`
- `tool.list_generated`
- `tool.upsert_generated`
- `tool.delete_generated`
- `tool.reload_generated`

They should also:

- report `supportsDynamicToolRegistration: true`
- report the generated tools directory path in `service.config_get`
- update `manifestHash` when generated definitions change
- execute generated code inside a stable host runtime

## Implemented today

| Platform | Generated payload | Execution runtime | Generated tools directory |
|---|---|---|---|
| Unity | C# editor tool scripts | Unity Editor compile/load cycle | Unity generated tool area inside the package state/runtime |
| Flutter | JSON descriptor plus async Dart source | Dart runner scripts launched by the Flutter adapter | `.ai_platform_agent/flutter/generated_tools/` |
| VS Code | JSON descriptor plus async JavaScript source | VS Code extension host | `.ai_platform_agent/vscode/generated_tools/` |
| JetBrains | JSON descriptor plus Groovy source | IntelliJ Platform plugin-hosted Groovy runtime | `.ai_platform_agent/jetbrains/generated_tools/` |
| Photoshop | JSON descriptor plus async JavaScript bridge source | Stable UXP plugin dispatcher | `<bridge-dir>/generated_tools/` |

## Next batch

| Order | Platform | Generated payload | Execution runtime | Planned directory |
|---|---|---|---|---|
| 1 | Unreal | JSON descriptor plus Python source | Unreal Python runtime | project state dir such as `Saved/AIPlatformAgent/generated_tools/` |
| 2 | Godot | JSON descriptor plus GDScript source | Stable editor/headless bridge plugin | addon/runtime generated tools directory |
| 3 | Cocos Creator | JSON descriptor plus JavaScript source | Stable editor extension dispatcher | extension-owned generated tools directory |

## Platform notes

- `Unreal`
  This is one of the most natural next targets because the official Python surface already matches the generated-tool pattern well.
- `Godot`
  Generated GDScript should route through one stable `EditorPlugin` or headless bridge instead of hot-swapping whole plugins.
- `Cocos Creator`
  The adapter should avoid generating new extensions per tool. One stable extension host should dispatch generated tool files.
