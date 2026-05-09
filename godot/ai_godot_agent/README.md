# AI Godot Agent

AI Godot Agent is the Godot adapter for the shared AI Platform Agent Framework.

It runs as a local Python service and exposes the same discovery-first protocol used by the other adapters:

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

## What the first Godot adapter covers

- Godot installation and version probing through the headless CLI
- Project summary inspection
- Scene listing and scene tree inspection from `.tscn` or `.scn`
- Node detail inspection inside a scene
- Node property inspection and mutation with save semantics
- Controlled scene node creation and deletion
- Paged service logs and recent tool calls

## Start

```bash
python -m ai_godot_agent.cli --root-dir /path/to/workspace
```

Useful flags:

- `--port 19779`
- `--port 19781`
- `--no-token`
- `--full-access`
- `--godot-executable /custom/path/to/godot`
- `--project-dir /path/to/godot/project`
- `--tool-timeout-seconds 180`

By default the token is stored at:

```text
<root-dir>/.ai_platform_agent/godot/token.txt
```
