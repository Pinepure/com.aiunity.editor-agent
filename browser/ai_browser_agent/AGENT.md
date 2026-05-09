# AI Browser Agent Protocol

This document is the built-in operating manual for AI agents that control browsers through AI Browser Agent.

## Connection

Default base URL:

```text
http://127.0.0.1:19778
```

Default token file:

```text
<RootDir>/.ai_platform_agent/browser/token.txt
```

Accepted token headers:

```http
X-AI-Agent-Token: <token>
X-Browser-Ai-Token: <token>
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
- Browser automation is implemented through Chrome DevTools Protocol and the official remote debugging HTTP endpoints.
- Screenshot output is a side-effecting tool and requires the server to start with `--full-access`.
- If Chrome is not already running with remote debugging enabled, the adapter can launch its own Chrome instance with `browser.chrome_launch`.
