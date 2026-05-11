# Minimal Integration Guide

This repository is a development monorepo. Runtime integration should stay platform-local.

The goal is:

- one adapter per host platform
- one local host-side package, plugin, or companion service per platform
- one shared HTTP protocol surface for the AI client
- no cross-platform runtime dependency between adapters

## General rule

When you ship one platform, only ship:

1. that platform's adapter
2. the smallest host-specific bootstrap it needs
3. optional shared documentation for the protocol

Do not ship:

- Unity code inside a Photoshop package
- Photoshop bridge code inside a VS Code extension
- JetBrains plugin code inside a Flutter package
- any other platform folder unless it is part of the target host itself

## Minimal packaging matrix

| Platform | Minimum host-side deliverable | Optional second piece | AI client connection | Do not include |
|---|---|---|---|---|
| Unity | `unity/com.aiunity.editor-agent/` Unity package | none | local HTTP to Unity Editor service | any non-Unity adapter |
| VS Code | `vscode/ai_vscode_agent/` extension | none | local HTTP to VS Code extension host | JetBrains/Photoshop/Unity code |
| JetBrains | `jetbrains/ai_jetbrains_agent/` IntelliJ Platform plugin | none | local HTTP to IDE plugin service | VS Code/Photoshop/Unity code |
| Photoshop | `photoshop/ai_photoshop_agent/ai_photoshop_agent/` Python companion | `photoshop/ai_photoshop_agent/uxp_plugin/` | local HTTP to companion service; companion bridges to UXP plugin | Unity/VS Code/JetBrains code |
| Flutter | `flutter/ai_flutter_agent/` Dart package | none | local HTTP to Flutter-side service | Unity/IDE/DCC adapters |
| Unreal | `unreal/ai_unreal_agent/` Unreal-side package/plugin | optional Remote Control bridge config | local HTTP to Unreal-side service | Unity/Godot/Cocos adapters |
| Godot | `godot/ai_godot_agent/` addon/plugin | none | local HTTP to Godot-side service | Unity/Unreal/Cocos adapters |
| Cocos Creator | planned Cocos extension host | optional generated tools runtime folder | local HTTP to Cocos-side service | any non-Cocos adapter |

## What “minimal integration” means per platform

### Unity

- Keep only the Unity package.
- Let Unity Editor own scene, prefab, asset, compile, and generated-tool behavior.
- The AI client only needs the HTTP endpoints and token.

### VS Code

- Keep only the extension project.
- Let the extension host own workspace, diagnostics, commands, tasks, and generated tools.
- The AI client should not need direct access to VS Code internals or any other adapter.

### JetBrains

- Keep only the IntelliJ Platform plugin project.
- Let the plugin own PSI, project model, VFS, run configurations, and generated tools.
- The AI client should only speak the shared protocol.

### Photoshop

- Keep only two Photoshop-specific pieces:
  - the Python companion service
  - the UXP plugin
- The companion exposes HTTP.
- The UXP plugin owns real Photoshop DOM execution.
- Nothing else from the monorepo should be required at runtime.

### Flutter

- Keep only the Dart package.
- Let the Flutter adapter own project inspection, CLI execution, and generated Dart tools.
- Do not pull in IDE adapters unless you intentionally want a separate IDE-side product.

### Unreal

- Keep only the Unreal-side adapter and any Unreal-local bridge config it needs.
- Let Unreal Python or the editor bridge own host execution.
- The AI client should not need Blender, Unity, or Godot code.

### Godot

- Keep only the Godot addon/plugin.
- Let the editor plugin or headless bridge own scene/resource execution.
- Keep generated tools in the Godot-owned addon/runtime area only.

### Cocos Creator

- Keep only one stable Cocos extension host when this adapter is implemented.
- Generated tools should be Cocos-local JavaScript files, not new full extensions per tool.

## Dynamic tool rule

Even when adapters support self-extension, the extension should stay platform-local:

- Unity generates Unity tools
- VS Code generates VS Code tools
- JetBrains generates JetBrains tools
- Photoshop generates Photoshop bridge tools
- Flutter should generate Flutter tools
- Unreal should generate Unreal tools
- Godot should generate Godot tools
- Cocos should generate Cocos tools

The AI should never need a cross-platform generated tool to complete a platform-local task.

## Recommended split when turning this monorepo into separate repositories

For each standalone repository, keep:

1. the platform folder
2. a platform README
3. a platform AGENT manual
4. only the shared docs that are actually needed for the protocol contract

You do not need to preserve the full monorepo layout inside every downstream repository.
