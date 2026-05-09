# AI Figma Agent Protocol

This document is the built-in operating manual for AI agents that control Figma workspaces through AI Figma Agent.

## Connection

Default base URL:

```text
http://127.0.0.1:19783
```

Default token file:

```text
<RootDir>/.ai_platform_agent/figma/token.txt
```

Accepted token headers:

```http
X-AI-Agent-Token: <token>
X-Figma-Ai-Token: <token>
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
- Figma access is performed through the official Figma REST API at `https://api.figma.com` or another configured base URL.
- Mutation tools are limited to the official REST surfaces that Figma exposes, such as comments and variables.
- File and variables tools require a configured Figma API token with the corresponding scopes.
