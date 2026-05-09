# AI WPF Agent Protocol

This document is the built-in operating manual for AI agents that control WPF projects through AI WPF Agent.

## Connection

Default base URL:

```text
http://127.0.0.1:19788
```

Accepted token headers:

```http
X-AI-Agent-Token: <token>
X-Wpf-Ai-Token: <token>
```

## Required AI workflow

1. Call `GET /health`.
2. Cache `manifestHash`.
3. Prefer `POST /manifest/search` or `GET /manifest/bundle/{id}` over full manifest loads.
4. Use `POST /tool/describe_many` before calling unfamiliar tools.
5. Use `GET /result/{handleId}` when a tool returns paged items or text chunks.

## Platform notes

- This adapter uses solution, project, XAML, and build tooling.
- It does not depend on fragile GUI automation.
- Mutation tools are restricted to explicit XAML attribute changes and require `--full-access`.
