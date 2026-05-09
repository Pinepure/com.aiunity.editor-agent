# AI Maya Agent

AI Maya Agent is the Maya adapter for the shared AI Platform Agent Framework.

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

## What the first Maya adapter covers

- Maya installation and version probing through `mayapy`
- Scene summary inspection
- DAG and dependency node listing
- Node detail inspection
- Attribute inspection and mutation with save semantics
- Controlled node creation and deletion
- Paged service logs and recent tool calls

## Start

```bash
python -m ai_maya_agent.cli --root-dir /path/to/workspace
```

Useful flags:

- `--port 19779`
- `--port 19782`
- `--no-token`
- `--full-access`
- `--mayapy-executable /custom/path/to/mayapy`
- `--default-scene-file /path/to/file.ma`
- `--tool-timeout-seconds 180`

By default the token is stored at:

```text
<root-dir>/.ai_platform_agent/maya/token.txt
```
