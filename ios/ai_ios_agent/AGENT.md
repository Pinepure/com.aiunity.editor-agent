# AI iOS Agent Protocol

This document is the built-in operating manual for AI agents that control iOS projects through AI iOS Agent.

## Connection

Default base URL:

```text
http://127.0.0.1:19789
```

Accepted token headers:

```http
X-AI-Agent-Token: <token>
X-Ios-Ai-Token: <token>
```

## Platform notes

- This adapter uses `xcodebuild` and `xcrun simctl`.
- It targets project/workspace, scheme, and simulator workflows.
- High-risk simulator/app mutation tools require `--full-access`.
