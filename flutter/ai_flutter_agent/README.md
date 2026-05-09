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

## Start

```bash
dart run bin/ai_flutter_agent.dart --project-root /path/to/flutter/project
```

Useful flags:

- `--port 19777`
- `--no-token`
- `--full-access`
- `--flutter-executable /custom/flutter`
- `--tool-timeout-ms 120000`

By default the token is stored at:

```text
<project-root>/.ai_platform_agent/token.txt
```
