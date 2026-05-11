# AI Rhino Agent

AI Rhino Agent is the Rhino adapter for the shared AI Platform Agent Framework.

It runs inside Rhino as a RhinoCommon plugin and exposes the shared discovery-first protocol over a local HTTP listener:

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

## What the first Rhino adapter covers

- Rhino plugin startup with `AtStartup` load mode
- Rhino version and runtime probing
- Active document summary inspection
- Layer, object, selection, and viewport listing
- Object detail inspection by id
- Controlled Rhino command execution behind the full-access guard
- Controlled layer visibility changes
- Paged service logs and recent tool calls

## Build

This adapter uses the official RhinoCommon NuGet package and multi-targets `net48` and `net7.0` so it can align with Rhino 8 runtime guidance.

```bash
dotnet build rhino/ai_rhino_agent/AiRhinoAgent.csproj
```

## Runtime state

The plugin stores its state under Rhino's plugin settings directory:

```text
<Rhino plugin settings>/ai_platform_agent/
```

Useful files:

- `config.json`
- `token.txt`

`config.json` can override:

```json
{
  "host": "127.0.0.1",
  "port": 19792,
  "requireToken": true,
  "fullAccessEnabled": false,
  "toolTimeoutMs": 120000
}
```
