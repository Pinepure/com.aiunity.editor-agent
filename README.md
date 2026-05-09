# AI Platform Agent Framework Monorepo

This repository is organized by platform adapter.

## Layout

- `docs/framework/`
  Shared framework and protocol documentation.
- `unity/com.aiunity.editor-agent/`
  Unity adapter package, kept in standard `com.xxx` package layout.
- `flutter/ai_flutter_agent/`
  Flutter adapter package.

## Packages

- Unity package entry: `unity/com.aiunity.editor-agent/package.json`
- Flutter package entry: `flutter/ai_flutter_agent/pubspec.yaml`

## Notes

- The repository root is a multi-platform container, not a Unity package root.
- Unity-specific documentation lives under `unity/com.aiunity.editor-agent/`.
- Shared protocol documentation lives under `docs/framework/`.
