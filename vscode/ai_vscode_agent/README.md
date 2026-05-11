# AI VS Code Agent

AI VS Code Agent is the VS Code adapter for the shared AI Platform Agent Framework.

It runs inside the VS Code extension host and exposes the same discovery-first protocol used by the other adapters:

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

## First-phase coverage

- workspace summary and open document state
- workspace file enumeration and text search
- document readback and document symbols
- diagnostics, commands, and tasks inspection
- controlled VS Code task execution
- dynamic generated-tool registration, reload, and execution inside the extension host

Current implementation status:

- Implemented as a local VS Code extension-host HTTP service
- Suitable for VS Code and VS Code-compatible editors such as Windsurf, subject to extension API compatibility
- Supports Unity-style generated tool growth through `.ai_platform_agent/vscode/generated_tools/`
- Supports workspace inspection, diagnostics, symbols, commands, tasks, and controlled task execution
- Does not depend on brittle GUI automation

## Start

Install the extension in VS Code and keep `aiPlatformAgent.vscode.enabled` turned on.

By default the service listens on:

```text
http://127.0.0.1:19790
```

The token file is stored in the extension global storage directory.

The exact token path is exposed in:

- `GET /health` under `platform.tokenFilePath`
- `service.config_get`
- the `AI VS Code Agent: Show Status` command

## What a human user should do

1. Unzip this package.
2. Install the unpacked extension into the target VS Code-compatible editor.
3. Open the target workspace in VS Code or Windsurf.
4. Ensure `aiPlatformAgent.vscode.enabled` is on.
5. If task execution or other high-risk tools are needed, enable `aiPlatformAgent.vscode.fullAccess`.
6. Use the `AI VS Code Agent: Show Status` command to confirm the local service URL and token path.
7. Keep the editor window open while the AI client connects to the local service.

Because this package is an unpacked VS Code extension folder, the most reliable distribution method is:

- send the zip
- let the recipient unzip it
- place the unpacked folder into the editor's local extensions directory
- restart the editor

## What the AI client should do

The AI side should use the adapter protocol instead of guessing IDE state:

1. `GET /health`
2. Cache `manifestHash`
3. `POST /manifest/search`
4. `POST /tool/describe_many`
5. `POST /call/{toolId}`
6. `GET /result/{handleId}` when paging is needed

The built-in AI operating manual is available from:

- local file: `AGENT.md`
- HTTP endpoint: `GET /agent`

Generated tool definitions are watched under `.ai_platform_agent/vscode/generated_tools/`, so manifest reloads are event-driven instead of rescanning the directory on every request.

## Minimal integration

For a standalone VS Code product, ship only this extension folder.

- keep `vscode/ai_vscode_agent/`
- do not bundle Unity, JetBrains, Photoshop, or other platform adapters
- let the VS Code extension host own all workspace and generated-tool behavior
- let the AI client talk only to the local HTTP service exposed by this extension
