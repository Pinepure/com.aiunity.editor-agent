# AI Unity Editor Agent Protocol

This document is the built-in operating manual for AI agents that control Unity through the AI Unity Editor Agent package.

The agent must treat `/manifest` as the source of truth. Never assume a tool exists. Always read the latest manifest before planning or executing Unity operations.

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

Health check:

```http
GET /health
```

Read current manifest:

```http
GET /manifest
X-Unity-Ai-Token: <token>
```

Call a tool:

```http
POST /call/{toolId}
X-Unity-Ai-Token: <token>
Content-Type: application/json

{...tool arguments...}
```

Read this manual through HTTP:

```http
GET /agent
X-Unity-Ai-Token: <token>
```

## 2. Required AI workflow

For every Unity task:

1. Call `GET /health`.
2. Call `GET /manifest`.
3. Check `compile.status` before executing complex operations.
4. Use an existing tool if the manifest contains one that fits the task.
5. If no suitable tool exists, generate a new Editor tool script and install it with `tool.upsert_script`.
6. Wait until `compile.status.isCompiling` is `false`.
7. Call `GET /manifest` again.
8. Call the new or existing tool.
9. Validate the result and inspect `compile.errors` or `service.log_recent` when something fails.

## 3. Tool method implementation contract

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

## 4. Standard response envelope

Tool call success:

```json
{
  "ok": true,
  "toolId": "asset.find",
  "durationMs": 12,
  "result": {
    "items": []
  }
}
```

Tool call failure:

```json
{
  "ok": false,
  "toolId": "asset.find",
  "error": "Human-readable error message"
}
```

## 5. Manifest conventions

`GET /manifest` returns:

```json
{
  "protocolVersion": "1.0",
  "serviceVersion": "0.1.0",
  "unityVersion": "...",
  "projectPath": "...",
  "serverUrl": "http://127.0.0.1:18777",
  "isCompiling": false,
  "hasCompileErrors": false,
  "tools": [
    {
      "id": "asset.find",
      "description": "Searches project assets.",
      "argsSchemaJson": "{...}",
      "returnSchemaJson": "{...}",
      "danger": "low",
      "requiresConfirmation": false,
      "declaringType": "...",
      "methodName": "..."
    }
  ]
}
```

The schema fields are JSON encoded strings. Parse them before presenting parameter forms to the user.

## 6. Built-in high-value tools

Common tools available in a fresh install:

- `system.health`: full main-thread health information.
- `manifest.get`: returns the same manifest as `GET /manifest`.
- `compile.status`: checks whether Unity is compiling, updating, playing, or has console errors.
- `compile.errors`: returns recent captured errors and warnings.
- `console.clear`: clears the Unity Console through internal reflection when available.
- `asset.refresh`: refreshes the AssetDatabase.
- `asset.find`: searches assets by Unity filter syntax, for example `t:Prefab Player`.
- `asset.dependencies`: returns assets referenced by a target asset.
- `asset.reverse_dependencies`: scans assets that reference a target asset.
- `asset.guid_to_path`: converts GUID to path.
- `asset.path_to_guid`: converts path to GUID.
- `asset.read_text`: reads text assets under `Assets/` with a size limit.
- `prefab.create_from_json`: creates a prefab from a JSON hierarchy manifest.
- `selection.get`: returns current Unity selection.
- `scene.create_empty`: creates an empty GameObject in the active scene.
- `scene.create_primitive`: creates a Unity primitive in the active scene.
- `tool.upsert_script`: installs or updates an AI-generated Editor tool script.
- `tool.list_generated`: lists generated tool scripts.
- `tool.delete_generated`: deletes one generated tool script after confirmation.
- `tool.get_template`: returns a safe generated tool template.
- `service.log_recent`: returns internal service logs.
- `service.call_recent`: returns recent tool calls.

## 7. Prefab JSON manifest

Call:

```http
POST /call/prefab.create_from_json
```

Example body:

```json
{
  "prefabPath": "Assets/Prefabs/AI_Player.prefab",
  "overwrite": true,
  "root": {
    "name": "AI_Player",
    "primitive": "Capsule",
    "transform": {
      "position": [0, 1, 0],
      "rotationEuler": [0, 0, 0],
      "scale": [1, 1, 1]
    },
    "components": [
      {
        "type": "UnityEngine.Rigidbody",
        "json": "{\"mass\":1.5}"
      }
    ],
    "children": [
      {
        "name": "Visual",
        "primitive": "Cube",
        "transform": {
          "position": [0, 0.5, 0],
          "scale": [0.5, 0.5, 0.5]
        }
      }
    ]
  }
}
```

Supported primitive values:

```text
Empty, Cube, Sphere, Capsule, Cylinder, Plane, Quad
```

Component rules:

- `type` may be a full type name such as `UnityEngine.Rigidbody`.
- If the component already exists, the tool patches it.
- If the component does not exist, the tool adds it when possible.
- `json` is passed to `EditorJsonUtility.FromJsonOverwrite`.

## 8. Asset reference search

Find prefabs:

```json
{
  "filter": "t:Prefab",
  "folders": ["Assets"],
  "maxResults": 100
}
```

Dependencies of an asset:

```json
{
  "path": "Assets/Prefabs/AI_Player.prefab",
  "recursive": true
}
```

Find assets that reference a texture or prefab:

```json
{
  "targetPath": "Assets/Art/Player.png",
  "searchFolders": ["Assets"],
  "filter": "",
  "recursive": true,
  "maxResults": 200
}
```

## 9. Compile diagnostics

Before modifying assets or scripts:

```json
{}
```

Call `compile.status`. If `isCompiling` is true, wait and retry. If `hasCompileErrors` is true, call `compile.errors`.

After calling `tool.upsert_script`:

1. Poll `compile.status` until `isCompiling == false`.
2. If `hasCompileErrors == true`, call `compile.errors`.
3. Fix the generated script with another `tool.upsert_script` call.
4. Reload `/manifest`.

## 10. Security rules for AI agents

- Never request a tool call that is not present in the manifest.
- Never bypass `requiresConfirmation`.
- Never write files outside generated tool folders.
- Never create tools that execute shell commands unless the user explicitly approves and the tool is marked high risk.
- Never read or expose secrets from project files unless the user explicitly asks.
- Prefer small, single-purpose tools over large unrestricted tools.
- Include argument schemas and return schemas for every generated tool.
- After adding or changing tools, refresh and re-read the manifest.

## 11. Minimal curl examples

Read token:

```bash
TOKEN=$(cat Library/AiUnityEditorAgent/token.txt)
```

Manifest:

```bash
curl -H "X-Unity-Ai-Token: $TOKEN" http://127.0.0.1:18777/manifest
```

Call asset search:

```bash
curl -X POST \
  -H "Content-Type: application/json" \
  -H "X-Unity-Ai-Token: $TOKEN" \
  -d '{"filter":"t:Prefab","folders":["Assets"],"maxResults":20}' \
  http://127.0.0.1:18777/call/asset.find
```

Create prefab:

```bash
curl -X POST \
  -H "Content-Type: application/json" \
  -H "X-Unity-Ai-Token: $TOKEN" \
  -d '{"prefabPath":"Assets/Prefabs/AI_Cube.prefab","overwrite":true,"root":{"name":"AI_Cube","primitive":"Cube"}}' \
  http://127.0.0.1:18777/call/prefab.create_from_json
```
