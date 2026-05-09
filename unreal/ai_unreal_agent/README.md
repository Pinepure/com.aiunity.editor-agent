# AI Unreal Agent

AI Unreal Agent is the Unreal adapter for the shared AI Platform Agent Framework.

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

## What the first Unreal adapter covers

- Unreal executable and project configuration probing
- Project summary inspection through `-ExecutePythonScript`
- Asset listing and asset detail inspection
- Level actor listing and actor detail inspection
- Controlled actor label and transform mutations
- Remote Control API status and preset discovery
- Paged service logs and recent tool calls

## Start

```bash
python -m ai_unreal_agent.cli --root-dir /path/to/workspace
```

Useful flags:

- `--port 19780`
- `--no-token`
- `--full-access`
- `--unreal-executable /custom/path/to/UnrealEditor`
- `--project-file /path/to/MyProject.uproject`
- `--remote-control-url http://127.0.0.1:30010`
- `--tool-timeout-seconds 300`

By default the token is stored at:

```text
<root-dir>/.ai_platform_agent/unreal/token.txt
```
