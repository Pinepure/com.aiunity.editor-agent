# AI Unity Editor Agent

AI Unity Editor Agent is a lightweight Editor-only Unity package that starts a local API service inside the Unity Editor. AI agents can discover the current tool manifest, call Unity Editor capabilities, add new Editor tools, create prefabs from JSON manifests, inspect compile diagnostics, and search asset dependency relationships.

## Install

1. Download and unzip the package.
2. In Unity, open **Window > Package Manager**.
3. Click **+ > Add package from disk...**.
4. Select `package.json` inside `com.aiunity.editor-agent`.
5. Open **Tools > AI Editor Agent > Control Center**.

## Default service

- URL: `http://127.0.0.1:18777`
- Token file: `<ProjectRoot>/Library/AiUnityEditorAgent/token.txt`
- Manifest: `GET /manifest`
- Call tool: `POST /call/{toolId}`
- Agent guide: `GET /agent` or the `AGENT.md` file in this package

All requests except `/health` require `X-Unity-Ai-Token` by default.

## Important safety defaults

- The server only listens on `127.0.0.1`.
- Generated tool scripts are restricted to `Assets/Editor/AiUnityEditorAgent/GeneratedTools/`.
- High-risk tools require a Unity confirmation dialog.
- Tool registration is generated from `[AiTool]` methods; the manifest is not hand-written.

See `AGENT.md` for the full AI calling specification.
