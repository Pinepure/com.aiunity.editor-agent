# AI Unity Editor Agent Protocol

This document is the built-in operating manual for AI agents that control Unity through the AI Unity Editor Agent package.

The agent must treat `manifestHash` as the capability cache key and must prefer search, bundle, describe, and paged result flows over repeatedly loading the full manifest.

## 1. Connection

Default base URL:

```text
http://127.0.0.1:18777
```

Default token file:

```text
<ProjectRoot>/Library/AiUnityEditorAgent/token.txt
```

Required request header unless disabled by the user in the Control Center:

```http
X-Unity-Ai-Token: <token>
```

Health and discovery metadata:

```http
GET /health
```

Lightweight manifest summary:

```http
GET /manifest
X-Unity-Ai-Token: <token>
```

Full manifest fallback:

```http
GET /manifest/full
X-Unity-Ai-Token: <token>
```

Manifest search:

```http
POST /manifest/search
X-Unity-Ai-Token: <token>
Content-Type: application/json

{"query":"find prefab dependencies","limit":6}
```

Focused capability bundles:

```http
GET /manifest/bundles
GET /manifest/bundle/asset-analysis
X-Unity-Ai-Token: <token>
```

Describe exact tool schemas:

```http
POST /tool/describe_many
X-Unity-Ai-Token: <token>
Content-Type: application/json

{"ids":["asset.find","asset.dependencies"]}
```

Call a tool:

```http
POST /call/{toolId}
X-Unity-Ai-Token: <token>
Content-Type: application/json

{...tool arguments...}
```

Read next pages or text chunks from a result handle:

```http
GET /result/{handleId}?offset=0&limit=20
X-Unity-Ai-Token: <token>
```

Read the concise brief:

```http
GET /agent/brief
X-Unity-Ai-Token: <token>
```

Read the full manual:

```http
GET /agent
X-Unity-Ai-Token: <token>
```

## 2. Required AI workflow

For every Unity task:

1. Call `GET /health`.
2. Cache the current `manifestHash`.
3. Reuse cached capability knowledge while `manifestHash` stays unchanged.
4. Prefer `POST /manifest/search` or `GET /manifest/bundle/{id}` to narrow candidate tools.
5. Call `POST /tool/describe_many` for exact schemas before using unfamiliar tools.
6. Call the selected tool through `POST /call/{toolId}`.
7. If a tool returns `resultHandle`, page additional data through `GET /result/{handleId}` instead of re-running the tool with larger limits.
8. Use `GET /manifest/full` only when search, bundles, and describe-many are insufficient.
9. If no suitable tool exists, generate a new Editor tool script with `tool.upsert_script`, wait for compile completion, then repeat discovery.

## 3. Protocol priorities

- Prefer `GET /health` over repeatedly loading manifests.
- Prefer `GET /manifest` over `GET /manifest/full`.
- Prefer `POST /manifest/search` over scanning all tools mentally.
- Prefer `POST /tool/describe_many` over reading schemas for tools you will not call.
- Prefer `GET /result/{handleId}` over asking a tool to return larger and larger payloads.
- Prefer `compile.snapshot` and `compile.errors_summary` before requesting verbose console entries.

## 4. Tool method implementation contract

All AI-generated tools must follow this C# shape:

```csharp
#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using AiUnity.EditorAgent;

namespace AiUnity.EditorAgent.Generated
{
    internal static class MyGeneratedTools
    {
        [Serializable]
        private sealed class Args
        {
            public string name;
        }

        [AiTool(
            "example.say_hello",
            "Returns a greeting.",
            @"{""type"":""object"",""properties"":{""name"":{""type"":""string""}}}",
            @"{""type"":""object"",""properties"":{""message"":{""type"":""string""}}}"
        )]
        public static string SayHello(string argsJson)
        {
            Args args = JsonUtility.FromJson<Args>(argsJson);
            string name = args == null || string.IsNullOrEmpty(args.name) ? "Unity" : args.name;
            return @"{""message"":" + AiJson.Quote("Hello, " + name) + "}";
        }
    }
}
#endif
```

Rules:

- Method must be `static`.
- Method must return `string`.
- Method must accept exactly one argument: `string argsJson`.
- Method must return a valid JSON value, usually a JSON object.
- Method must have `[AiTool]`.
- Tool IDs must be unique and stable, using dotted names such as `scene.create_empty` or `asset.reverse_dependencies`.
- Use `JsonUtility.FromJson<T>()` for simple arguments.
- Use `AiJson.Quote()` and `AiJson.Escape()` when composing JSON manually.
- High-risk tools must set `Danger = AiToolDanger.High` and `RequiresConfirmation = true`.
- Generated tools should use the namespace `AiUnity.EditorAgent.Generated`.
- Generated scripts must be installed through `tool.upsert_script`; do not write outside `Assets/Editor/AiUnityEditorAgent/GeneratedTools/`.

## 5. Manifest conventions

`GET /manifest` returns the lightweight summary:

```json
{
  "protocolVersion": "2.0",
  "serviceVersion": "0.2.0",
  "manifestHash": "7c3d...",
  "toolCount": 35,
  "namespaces": [
    { "id": "asset", "count": 7 }
  ],
  "tools": [
    {
      "id": "asset.find",
      "namespaceId": "asset",
      "description": "Searches project assets...",
      "danger": "low",
      "requiresConfirmation": false
    }
  ]
}
```

`GET /manifest/full` returns the full fallback manifest with `argsSchemaJson`, `returnSchemaJson`, and source metadata.

`POST /tool/describe_many` returns exact schemas for the requested tool ids:

```json
{
  "manifestHash": "7c3d...",
  "requestedCount": 2,
  "returnedCount": 2,
  "missingIds": [],
  "tools": [
    {
      "id": "asset.find",
      "namespaceId": "asset",
      "argsSchemaJson": "{...}",
      "returnSchemaJson": "{...}"
    }
  ]
}
```

Only load full schemas for tools you actually plan to call.

## 6. Search and bundle conventions

Use manifest search for open-ended tasks:

```json
{
  "query": "inspect prefab dependencies",
  "limit": 6
}
```

Use bundle loading for focused workflows:

- `asset-analysis`
- `scene-editing`
- `prefab-authoring`
- `tool-authoring`
- `service-diagnostics`

Search results include `score` and `whyMatched`. Use those to shortlist tools before calling `describe_many`.

## 7. Result handle conventions

Large tool responses may return this shape:

```json
{
  "summary": {
    "source": "asset.find",
    "totalFound": 183
  },
  "returned": 20,
  "pageSize": 20,
  "total": 183,
  "hasMore": true,
  "resultHandle": "abcd1234",
  "items": []
}
```

When `resultHandle` is present, continue with:

```http
GET /result/abcd1234?offset=20&limit=20
```

Text readers such as `asset.read_text` may return chunked `content` plus `resultHandle`.

## 8. Built-in high-value tools

Common tools available in a fresh install:

- `system.health`
- `manifest.get`
- `manifest.get_summary`
- `manifest.search`
- `manifest.list_bundles`
- `manifest.get_bundle`
- `tool.describe_many`
- `agent.get_brief`
- `agent.get_manual`
- `compile.status`
- `compile.snapshot`
- `compile.errors`
- `compile.errors_summary`
- `console.clear`
- `asset.refresh`
- `asset.find`
- `asset.dependencies`
- `asset.reverse_dependencies`
- `asset.guid_to_path`
- `asset.path_to_guid`
- `asset.read_text`
- `prefab.create_from_json`
- `selection.get`
- `scene.create_empty`
- `scene.create_primitive`
- `tool.upsert_script`
- `tool.list_generated`
- `tool.delete_generated`
- `tool.get_template`
- `service.log_recent`
- `service.call_recent`

## 9. Compile diagnostics

Prefer this order:

1. `compile.snapshot`
2. `compile.errors_summary`
3. `compile.errors` with `includeStackTrace=false`
4. `compile.errors` with `includeStackTrace=true` only when stack details are needed

After calling `tool.upsert_script`:

1. Poll `compile.status` until `isCompiling == false`.
2. If `hasCompileErrors == true`, call `compile.snapshot`.
3. If the summary is insufficient, call `compile.errors_summary` or paged `compile.errors`.
4. Resume discovery through `GET /health` and `POST /manifest/search`.

## 10. Security rules for AI agents

- Never request a tool call that is not present in the latest discovery results or full manifest.
- Never bypass `requiresConfirmation`.
- Never write files outside generated tool folders.
- Never create tools that execute shell commands unless the user explicitly approves and the tool is marked high risk.
- Never read or expose secrets from project files unless the user explicitly asks.
- Prefer small, single-purpose tools over large unrestricted tools.
- Include argument schemas and return schemas for every generated tool.
- After adding or changing tools, re-check `manifestHash` before assuming old capability caches still apply.

## 11. Minimal curl examples

Read token:

```bash
TOKEN=$(cat Library/AiUnityEditorAgent/token.txt)
```

Health:

```bash
curl -H "X-Unity-Ai-Token: $TOKEN" http://127.0.0.1:18777/health
```

Search candidate tools:

```bash
curl -X POST \
  -H "Content-Type: application/json" \
  -H "X-Unity-Ai-Token: $TOKEN" \
  -d '{"query":"find prefab dependencies","limit":6}' \
  http://127.0.0.1:18777/manifest/search
```

Describe exact schemas:

```bash
curl -X POST \
  -H "Content-Type: application/json" \
  -H "X-Unity-Ai-Token: $TOKEN" \
  -d '{"ids":["asset.find","asset.dependencies"]}' \
  http://127.0.0.1:18777/tool/describe_many
```

Call asset search:

```bash
curl -X POST \
  -H "Content-Type: application/json" \
  -H "X-Unity-Ai-Token: $TOKEN" \
  -d '{"filter":"t:Prefab","folders":["Assets"],"maxResults":100,"pageSize":20}' \
  http://127.0.0.1:18777/call/asset.find
```

Read the next page from a result handle:

```bash
curl -H "X-Unity-Ai-Token: $TOKEN" \
  "http://127.0.0.1:18777/result/<HANDLE>?offset=20&limit=20"
```
