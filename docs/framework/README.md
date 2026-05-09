# AI Platform Agent Framework

AI Platform Agent Framework is the shared architecture behind this repository's platform adapters.

It standardizes how an AI agent discovers host capabilities, inspects exact tool schemas, calls tools, pages through large responses, and learns which platform-specific execution features are available.

## Core contract

- `GET /health`
  Returns protocol metadata, `manifestHash`, platform identity, token header names, and capability flags.
- `GET /manifest`
  Returns the lightweight tool summary without full schemas.
- `GET /manifest/full`
  Returns the full fallback manifest.
- `POST /manifest/search`
  Narrows the candidate tool set before an agent loads exact schemas.
- `GET /manifest/bundles`
  Lists focused capability bundles.
- `GET /manifest/bundle/{id}`
  Returns a summary manifest for one bundle.
- `POST /tool/describe_many`
  Returns exact schemas only for the selected tools.
- `POST /call/{toolId}`
  Invokes one tool with JSON arguments.
- `GET /result/{handleId}`
  Pages through large item lists or text outputs.
- `GET /agent/brief`
  Returns the concise workflow.
- `GET /agent`
  Returns the full platform-specific AI operating manual.

## Shared design rules

- Agents should treat `manifestHash` as the capability cache key.
- Agents should prefer search, bundles, and describe-many over loading the full manifest on every task.
- Tools should expose stable ids, JSON argument schemas, JSON return schemas, and risk metadata.
- Large outputs should return a `resultHandle` instead of inflating one response indefinitely.
- Platform adapters can expose optional features such as dynamic tool registration, but they must declare those capabilities explicitly through `health` and `service.config_get`.

## Repository layout

- `unity/com.aiunity.editor-agent/`
  Unity adapter implementation, kept as a standard Unity package.
- `flutter/ai_flutter_agent/`
  Flutter adapter implementation.
- `browser/ai_browser_agent/`
  Browser adapter implementation.
- `blender/ai_blender_agent/`
  Blender adapter implementation.
- `figma/ai_figma_agent/`
  Figma adapter implementation.
- `godot/ai_godot_agent/`
  Godot adapter implementation.
- `houdini/ai_houdini_agent/`
  Houdini adapter implementation.
- `maya/ai_maya_agent/`
  Maya adapter implementation.
- `unreal/ai_unreal_agent/`
  Unreal adapter implementation.
- `android/ai_android_agent/`
  Android adapter implementation.
- `wpf/ai_wpf_agent/`
  WPF adapter implementation.
- `ios/ai_ios_agent/`
  iOS adapter implementation.
- `docs/framework/`
  Shared protocol and architecture docs.

The prioritized platform rollout is tracked in `docs/framework/PLATFORM_ROADMAP.md`.

## Adapter model

Each adapter owns the host-specific work:

- Unity owns editor state, assets, scenes, prefabs, compile state, generated editor tools, and confirmation dialogs.
- Flutter owns project inspection, source search, file-safe mutations, Flutter CLI workflows, and widget indexing.
- Browser owns Chrome discovery, page targets, runtime evaluation, DOM inspection, and screenshot capture through Chrome DevTools Protocol.
- Blender owns `.blend` loading, scene inspection, object inspection, render execution, and controlled file mutations through Blender's Python API.
- Figma owns token-backed file, node, comment, variable, and render/image inspection plus the officially writable REST mutation surfaces.
- Godot owns project inspection, scene inspection, node/property inspection, and controlled scene mutations through headless Godot script execution.
- Houdini owns `.hip` loading, node graph inspection, parameter inspection, and controlled node or parameter mutations through HOM.
- Maya owns scene inspection, node and attribute inspection, and controlled scene mutations through Maya standalone APIs.
- Unreal owns project, asset, and actor inspection through Unreal Python, plus live Remote Control API discovery for running editors.
- Android owns SDK inspection, ADB device/package/log workflows, Gradle build and test execution, and controlled emulator or app actions.
- WPF owns solution inspection, XAML inspection, XAML search, controlled XAML mutation, and build/test execution through `dotnet` and `msbuild`.
- iOS owns Xcode project/workspace inspection, scheme discovery, simulator inspection, xcodebuild build/test execution, and controlled simulator app workflows.

They share the protocol shape, discovery flow, manifest conventions, result paging conventions, and AI workflow guidance.
