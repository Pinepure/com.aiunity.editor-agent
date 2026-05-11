# AI VS Code Agent Protocol

This document is the built-in operating manual for AI agents that control VS Code through AI VS Code Agent.

## Connection

Default base URL:

```text
http://127.0.0.1:19790
```

Accepted token headers:

```http
X-AI-Agent-Token: <token>
X-Vscode-Ai-Token: <token>
```

Token path discovery:

- read `platform.tokenFilePath` from `GET /health`
- or use `service.config_get` after authorization
- or have the human operator run `AI VS Code Agent: Show Status`

## Required AI workflow

1. Call `GET /health`.
2. Cache `manifestHash`.
3. Reuse cached capabilities while `manifestHash` stays unchanged.
4. Prefer `POST /manifest/search` or `GET /manifest/bundle/{id}` to narrow candidate tools.
5. Use `POST /tool/describe_many` for exact schemas before calling unfamiliar tools.
6. Use `POST /call/{toolId}` for the actual work.
7. Use `GET /result/{handleId}` when a tool returns a paged result or text chunk.
8. Use `GET /manifest/full` only as a fallback.

## Platform notes

- This adapter runs inside the official VS Code extension host.
- It uses the VS Code Extension API for workspace, diagnostics, symbols, commands, and tasks.
- High-risk tools such as task execution require `aiPlatformAgent.vscode.fullAccess`.
- This adapter supports dynamic tool registration through `tool.get_template`, `tool.upsert_generated`, `tool.list_generated`, `tool.delete_generated`, and `tool.reload_generated`.
- Generated tool definitions live under `.ai_platform_agent/vscode/generated_tools/` in the workspace root.
- Generated tool source runs as async JavaScript with `(args, host, require, console)` and can use `host.vscode`, `host.fs`, `host.path`, and helper methods exposed by the adapter.
- The local token file lives under the extension global storage directory and is exposed through `GET /health` as `platform.tokenFilePath`.
