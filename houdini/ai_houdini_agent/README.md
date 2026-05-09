# AI Houdini Agent

AI Houdini Agent is the Houdini adapter for the shared AI Platform Agent Framework.

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

## What the first Houdini adapter covers

- Houdini installation and version probing through `hython`
- `.hip` file summary inspection
- Root network, node, and child node listing through HOM (`hou`)
- Node detail inspection
- Parameter inspection and mutation with save semantics
- Controlled node creation and deletion
- Paged service logs and recent tool calls

## Start

```bash
python -m ai_houdini_agent.cli --root-dir /path/to/workspace
```

Useful flags:

- `--port 19779`
- `--no-token`
- `--full-access`
- `--hython-executable /custom/path/to/hython`
- `--default-hip-file /path/to/file.hip`
- `--tool-timeout-seconds 180`

By default the token is stored at:

```text
<root-dir>/.ai_platform_agent/houdini/token.txt
```
