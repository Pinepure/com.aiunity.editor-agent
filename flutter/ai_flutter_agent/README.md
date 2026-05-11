# AI Flutter Agent

AI Flutter Agent is the Flutter adapter for the shared AI Platform Agent Framework.

It runs as a local Dart server alongside a Flutter project and exposes the same discovery-first tool protocol used by the Unity adapter:

- `GET /health`
- `GET /manifest`
- `GET /manifest/full`
- `POST /manifest/search`
- `GET /manifest/bundles`
- `GET /manifest/bundle/{id}`
- `POST /tool/describe_many`
- `POST /call/{toolId}`
- `GET /result/{handleId}`
- `GET /agent/brief`
- `GET /agent`

## What the first Flutter adapter covers

- Flutter project metadata inspection
- File listing, file reading, and text search with safe path boundaries
- Controlled file writes and deletes behind `--full-access`
- `flutter pub get`
- `flutter analyze`
- `flutter test`
- `flutter doctor -v`
- Widget class indexing from `lib/`
- Paged service logs and recent tool calls
- Generated Flutter tool registration, validation, reload, and execution through Dart runner scripts

## Start

```bash
dart run bin/ai_flutter_agent.dart --project-root /path/to/flutter/project
```

Useful flags:

- `--port 19777`
- `--no-token`
- `--full-access`
- `--flutter-executable /custom/flutter`
- `--dart-executable /custom/dart`
- `--tool-timeout-ms 120000`

By default the token is stored at:

```text
<project-root>/.ai_platform_agent/flutter/token.txt
```

Generated tool definitions live under:

```text
<project-root>/.ai_platform_agent/flutter/generated_tools/
```

## Dynamic generated tools

- `service.config_get` and `GET /health` advertise `supportsDynamicToolRegistration: true`.
- Built-in management tools are `tool.get_template`, `tool.list_generated`, `tool.upsert_generated`, `tool.delete_generated`, and `tool.reload_generated`.
- Generated tool definitions are validated by compiling a generated Dart runner before they are accepted, unless `validate: false` is passed to `tool.upsert_generated`.
- Generated tool source runs as async Dart with `(args, host)` and can read project files, mutate files, search text, and execute `flutter`, `dart`, or arbitrary commands through the host helpers.

## Minimal integration

For a standalone Flutter product, ship only this Dart package.

- keep `flutter/ai_flutter_agent/`
- do not bundle Unity, VS Code, JetBrains, Photoshop, or other platform adapters
- let this adapter own project inspection, CLI execution, generated Dart tools, and verification
- let the AI client talk only to the local HTTP service exposed by this package
