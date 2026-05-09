# AI Figma Agent

AI Figma Agent is the Figma adapter for the shared AI Platform Agent Framework.

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

## What the first Figma adapter covers

- Figma token status and current user inspection
- File JSON, node JSON, metadata, images, comments, and local variables queries
- Comment creation and deletion
- Local variables mutation through the Variables API
- Paged service logs and recent tool calls

## Start

```bash
python -m ai_figma_agent.cli --root-dir /path/to/workspace
```

Useful flags:

- `--port 19783`
- `--no-token`
- `--full-access`
- `--figma-base-url https://api.figma.com`
- `--figma-token your-token`
- `--tool-timeout-seconds 120`

By default the token is stored at:

```text
<root-dir>/.ai_platform_agent/figma/token.txt
```
