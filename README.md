# AI Platform Agent Framework Monorepo

This repository is organized by platform adapter.

## Layout

- `docs/framework/`
  Shared framework and protocol documentation.
- `unity/com.aiunity.editor-agent/`
  Unity adapter package, kept in standard `com.xxx` package layout.
- `flutter/ai_flutter_agent/`
  Flutter adapter package.
- `browser/ai_browser_agent/`
  Browser adapter package built on Chrome DevTools Protocol.
- `blender/ai_blender_agent/`
  Blender adapter package built on Blender Python automation.
- `figma/ai_figma_agent/`
  Figma adapter package built on the official Figma REST API.
- `vscode/ai_vscode_agent/`
  VS Code adapter package built as a local VS Code extension and HTTP service.
- `jetbrains/ai_jetbrains_agent/`
  JetBrains adapter package built as an IntelliJ Platform plugin and HTTP service.
- `godot/ai_godot_agent/`
  Godot adapter package built on headless Godot CLI and GDScript bridge execution.
- `houdini/ai_houdini_agent/`
  Houdini adapter package built on `hython` and HOM (`hou`).
- `maya/ai_maya_agent/`
  Maya adapter package built on `mayapy` and Maya standalone APIs.
- `rhino/ai_rhino_agent/`
  Rhino adapter package built as a RhinoCommon plugin and HTTP service.
- `revit/ai_revit_agent/`
  Revit adapter package built as a Revit `IExternalApplication` add-in and HTTP service.
- `photoshop/ai_photoshop_agent/`
  Photoshop adapter package built as a Python companion service plus a UXP plugin bridge.
- `cocos/`
  Planned Cocos Creator adapter space.
- `unreal/ai_unreal_agent/`
  Unreal adapter package built on Unreal Python and Remote Control APIs.
- `android/ai_android_agent/`
  Android adapter package built on ADB, Gradle, and emulator tooling.
- `wpf/ai_wpf_agent/`
  WPF adapter package built on solution, XAML, `dotnet`, and `msbuild` tooling.
- `ios/ai_ios_agent/`
  iOS adapter package built on `xcodebuild` and `xcrun simctl`.

## Packages

- Unity package entry: `unity/com.aiunity.editor-agent/package.json`
- Flutter package entry: `flutter/ai_flutter_agent/pubspec.yaml`
- Browser package entry: `browser/ai_browser_agent/package.json`
- Blender package entry: `blender/ai_blender_agent/pyproject.toml`
- Figma package entry: `figma/ai_figma_agent/pyproject.toml`
- VS Code package entry: `vscode/ai_vscode_agent/package.json`
- JetBrains package entry: `jetbrains/ai_jetbrains_agent/build.gradle.kts`
- Godot package entry: `godot/ai_godot_agent/pyproject.toml`
- Houdini package entry: `houdini/ai_houdini_agent/pyproject.toml`
- Maya package entry: `maya/ai_maya_agent/pyproject.toml`
- Rhino package entry: `rhino/ai_rhino_agent/AiRhinoAgent.csproj`
- Revit package entry: `revit/ai_revit_agent/AiRevitAgent.csproj`
- Photoshop companion entry: `photoshop/ai_photoshop_agent/pyproject.toml`
- Photoshop plugin entry: `photoshop/ai_photoshop_agent/uxp_plugin/manifest.json`
- Unreal package entry: `unreal/ai_unreal_agent/pyproject.toml`
- Android package entry: `android/ai_android_agent/pyproject.toml`
- WPF package entry: `wpf/ai_wpf_agent/pyproject.toml`
- iOS package entry: `ios/ai_ios_agent/pyproject.toml`

## Notes

- The repository root is a multi-platform container, not a Unity package root.
- Unity-specific documentation lives under `unity/com.aiunity.editor-agent/`.
- Shared protocol documentation lives under `docs/framework/`.
- Platform ranking and implementation status live in `docs/framework/PLATFORM_ROADMAP.md`.
- Dynamic self-extension rollout details live in `docs/framework/DYNAMIC_TOOL_ROLLOUT.md`.
- Minimal per-platform packaging guidance lives in `docs/framework/MINIMAL_INTEGRATION_GUIDE.md`.

## Dynamic tool registration status

- Dynamic self-extension is fully implemented today for `Unity`, `Flutter`, `VS Code`, `JetBrains`, and `Photoshop`.
- `Unreal`, `Godot`, and planned `Cocos Creator` are the next dynamic-tool batch because their official host/runtime surfaces are strong enough to support generated tool layers instead of brittle UI automation.
- Other implemented adapters still expose useful host tools, but they are currently fixed toolsets rather than self-growing tool runtimes.
