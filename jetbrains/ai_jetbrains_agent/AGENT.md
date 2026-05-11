# AI JetBrains Agent Protocol

This document is the built-in operating manual for AI agents that control JetBrains IDEs through AI JetBrains Agent.

## Connection

Default base URL:

```text
http://127.0.0.1:19791
```

Accepted token headers:

```http
X-AI-Agent-Token: <token>
X-Jetbrains-Ai-Token: <token>
```

## Platform notes

- This adapter runs inside the IntelliJ Platform process.
- It uses official Project Model, VFS, PSI, and Run Configuration APIs.
- Mutation and execution tools should remain guarded by `--full-access` equivalents in future revisions.
- This adapter supports dynamic tool registration through `tool.get_template`, `tool.upsert_generated`, `tool.list_generated`, `tool.delete_generated`, and `tool.reload_generated`.
- Generated tool definitions live under `.ai_platform_agent/jetbrains/generated_tools/` inside the adapter state directory.
- Generated tool source runs as Groovy with bindings `args`, `host`, and `json`.
