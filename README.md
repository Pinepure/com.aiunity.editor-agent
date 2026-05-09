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
- `godot/ai_godot_agent/`
  Godot adapter package built on headless Godot CLI and GDScript bridge execution.
- `houdini/ai_houdini_agent/`
  Houdini adapter package built on `hython` and HOM (`hou`).
- `maya/ai_maya_agent/`
  Maya adapter package built on `mayapy` and Maya standalone APIs.
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
- Godot package entry: `godot/ai_godot_agent/pyproject.toml`
- Houdini package entry: `houdini/ai_houdini_agent/pyproject.toml`
- Maya package entry: `maya/ai_maya_agent/pyproject.toml`
- Unreal package entry: `unreal/ai_unreal_agent/pyproject.toml`
- Android package entry: `android/ai_android_agent/pyproject.toml`
- WPF package entry: `wpf/ai_wpf_agent/pyproject.toml`
- iOS package entry: `ios/ai_ios_agent/pyproject.toml`

## Notes

- The repository root is a multi-platform container, not a Unity package root.
- Unity-specific documentation lives under `unity/com.aiunity.editor-agent/`.
- Shared protocol documentation lives under `docs/framework/`.
- Platform ranking and implementation status live in `docs/framework/PLATFORM_ROADMAP.md`.
