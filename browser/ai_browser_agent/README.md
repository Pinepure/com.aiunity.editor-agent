# AI Browser Agent

AI Browser Agent is the Browser adapter for the shared AI Platform Agent Framework.

It runs as a local Node service and exposes the same discovery-first protocol used by the other adapters:

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

## What the first Browser adapter covers

- Chrome discovery through remote debugging HTTP endpoints
- Chrome launch with a dedicated profile for the adapter
- Page target listing, opening, activation, and closing
- Runtime JavaScript evaluation
- DOM document inspection and selector queries
- Screenshot capture through Chrome DevTools Protocol
- Paged service logs and recent tool calls

## Start

```bash
node ./bin/ai_browser_agent.mjs --root-dir /path/to/workspace
```

Useful flags:

- `--port 19778`
- `--no-token`
- `--full-access`
- `--chrome-host 127.0.0.1`
- `--chrome-port 9222`
- `--chrome-executable /custom/path/to/chrome`
- `--tool-timeout-ms 120000`

By default the token is stored at:

```text
<root-dir>/.ai_platform_agent/browser/token.txt
```
