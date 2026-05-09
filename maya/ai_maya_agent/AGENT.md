# AI Maya Agent Protocol

This document is the built-in operating manual for AI agents that control Maya projects through AI Maya Agent.

## Connection

Default base URL:

```text
http://127.0.0.1:19782
```

Default token file:

```text
<RootDir>/.ai_platform_agent/maya/token.txt
```

Accepted token headers:

```http
X-AI-Agent-Token: <token>
X-Maya-Ai-Token: <token>
```

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

- This adapter currently reports `supportsDynamicToolRegistration: false`.
- Maya execution is performed through the official `mayapy` interpreter and Maya standalone APIs.
- Mutation tools write scene files and therefore require the server to start with `--full-access`.
- Read-only tools can inspect either the configured default scene file or a per-call `sceneFile` argument.
