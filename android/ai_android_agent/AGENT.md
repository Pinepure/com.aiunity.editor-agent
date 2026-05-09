# AI Android Agent Protocol

This document is the built-in operating manual for AI agents that control Android projects and devices through AI Android Agent.

## Connection

Default base URL:

```text
http://127.0.0.1:19787
```

Default token file:

```text
<RootDir>/.ai_platform_agent/android/token.txt
```

Accepted token headers:

```http
X-AI-Agent-Token: <token>
X-Android-Ai-Token: <token>
```

## Required AI workflow

1. Call `GET /health`.
2. Cache `manifestHash`.
3. Reuse cached capabilities while `manifestHash` stays unchanged.
4. Prefer `POST /manifest/search` or `GET /manifest/bundle/{id}` to narrow candidate tools.
5. Use `POST /tool/describe_many` for exact schemas before calling unfamiliar tools.
6. Use `POST /call/{toolId}` for the actual work.
7. Use `GET /result/{handleId}` when a tool returns paged items or text chunks.
8. Use `GET /manifest/full` only as a fallback.

## Platform notes

- This adapter uses official Android SDK tooling, not UI automation.
- Device and app tools run through `adb`.
- Build and test tools run through `gradlew` when present, or the configured `gradle` executable.
- Emulator tools run through the official `emulator` CLI.
- High-risk mutation tools require `--full-access`.
