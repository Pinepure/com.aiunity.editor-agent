# AI Photoshop Agent

AI Photoshop Agent is the Adobe Photoshop adapter for the shared AI Platform Agent Framework.

It is split into two parts:

- a local Python companion service that exposes the shared discovery-first HTTP protocol
- a Photoshop UXP plugin that executes the real host operations through the official Photoshop DOM and UXP file APIs

## What the first Photoshop adapter covers

- Bridge-folder health and heartbeat inspection
- Open document and active-document inspection
- Layer tree listing and per-layer detail inspection
- Controlled layer visibility changes
- Controlled text-layer content changes
- Controlled document save
- Paged service logs and recent tool calls
- Generated bridge-tool registration, reload, and execution through the stable UXP dispatcher

## Start the companion service

```bash
python -m ai_photoshop_agent.cli --root-dir /path/to/workspace --bridge-dir /path/to/bridge
```

Useful flags:

- `--port 19794`
- `--no-token`
- `--full-access`
- `--tool-timeout-seconds 120`
- `--poll-interval-seconds 0.5`

By default the service token is stored at:

```text
<root-dir>/.ai_platform_agent/photoshop/token.txt
```

## Load the Photoshop plugin

Load `photoshop/ai_photoshop_agent/uxp_plugin/` with Adobe's UXP Developer Tool.

Inside the plugin panel:

1. Choose the same bridge folder path that the companion service uses.
2. Keep the panel open while the service is active.
3. The plugin will publish heartbeat status and process requests from the `requests/` folder.

## Dynamic generated tools

- `service.config_get` and `GET /health` advertise `supportsDynamicToolRegistration: true`.
- Generated tool definitions live under `<bridge-dir>/generated_tools/`.
- Built-in management tools are `tool.get_template`, `tool.list_generated`, `tool.upsert_generated`, `tool.delete_generated`, and `tool.reload_generated`.
- Generated source executes inside the UXP plugin as async JavaScript with `(args, host, require, console)`.

## Minimal integration

For a standalone Photoshop product, ship only the Photoshop-specific pair:

- `photoshop/ai_photoshop_agent/ai_photoshop_agent/` for the Python companion service
- `photoshop/ai_photoshop_agent/uxp_plugin/` for the UXP plugin

Do not bundle Unity, VS Code, JetBrains, or any other platform adapter.

The companion owns the shared HTTP protocol.
The UXP plugin owns real Photoshop DOM execution.
