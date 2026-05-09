# AI Platform Agent Framework Protocol

This document describes the platform-neutral contract shared by the adapters in this repository.

## Required workflow

1. Call `GET /health`.
2. Cache the returned `manifestHash`.
3. Reuse cached capability knowledge while `manifestHash` stays unchanged.
4. Use `POST /manifest/search` or `GET /manifest/bundle/{id}` to narrow candidate tools.
5. Use `POST /tool/describe_many` for exact schemas before calling unfamiliar tools.
6. Call `POST /call/{toolId}` with JSON arguments.
7. If the tool returns `resultHandle`, continue through `GET /result/{handleId}` instead of rerunning the tool with larger and larger limits.
8. Use `GET /manifest/full` only as a fallback.

## Required `health` fields

- `framework`
- `service`
- `serviceId`
- `version`
- `platformId`
- `protocolVersion`
- `requiresToken`
- `acceptedTokenHeaders`
- `manifestHash`
- `toolCount`
- `namespaces`
- `supportsManifestSearch`
- `supportsDescribeMany`
- `supportsResultHandles`
- `supportsBundles`
- `supportsDynamicToolRegistration`
- `recommendedFlow`
- `paths`

## Optional platform capabilities

Adapters may expose platform-specific features, but they must declare them rather than assuming the AI already knows:

- Dynamic tool registration
- Compile snapshots
- Runtime tree inspection
- Asset graph inspection
- Source mutation helpers
- Build, test, and diagnostics hooks

## Token headers

The shared primary header is:

```http
X-AI-Agent-Token: <token>
```

Adapters may additionally accept a legacy adapter-specific header for backward compatibility. The accepted names must be reported by `health` and `service.config_get`.
