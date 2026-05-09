# AI Unreal Agent Protocol

This document is the built-in operating manual for AI agents that control Unreal projects through AI Unreal Agent.

## Connection

Default base URL:

```text
http://127.0.0.1:19780
```

Default token file:

```text
<RootDir>/.ai_platform_agent/unreal/token.txt
```

Accepted token headers:

```http
X-AI-Agent-Token: <token>
X-Unreal-Ai-Token: <token>
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
- Unreal execution is performed through the official Unreal Editor Python executor (`-ExecutePythonScript`) and the Remote Control HTTP API.
- Mutation tools affect project assets or levels and therefore require the server to start with `--full-access`.
- Python-backed tools require either a configured default `.uproject` file or a per-call `projectFile` argument.
