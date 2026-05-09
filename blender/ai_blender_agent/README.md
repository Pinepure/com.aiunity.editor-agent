# AI Blender Agent

AI Blender Agent is the Blender adapter for the shared AI Platform Agent Framework.

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

## What the first Blender adapter covers

- Blender installation and version probing
- `.blend` file summary inspection
- Scene, collection, and object listing through Blender Python
- Object detail inspection
- Controlled object transform mutations with save semantics
- Still image rendering to an output path
- Paged service logs and recent tool calls

## Start

```bash
python -m ai_blender_agent.cli --root-dir /path/to/workspace
```

Useful flags:

- `--port 19779`
- `--no-token`
- `--full-access`
- `--blender-executable /custom/path/to/blender`
- `--default-blend-file /path/to/file.blend`
- `--tool-timeout-seconds 180`

By default the token is stored at:

```text
<root-dir>/.ai_platform_agent/blender/token.txt
```
