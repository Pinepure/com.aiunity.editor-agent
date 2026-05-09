# Platform Roadmap

This repository is a multi-platform host-agent monorepo.

Each platform adapter lives in its own top-level folder and owns its host-specific implementation, packaging, and AI operating manual.

## Directory contract

```text
repo-root/
  docs/framework/
  unity/com.aiunity.editor-agent/
  flutter/ai_flutter_agent/
  browser/ai_browser_agent/
  blender/ai_blender_agent/
  figma/ai_figma_agent/
  godot/ai_godot_agent/
  maya/ai_maya_agent/
  android/ai_android_agent/
  wpf/ai_wpf_agent/
  ios/ai_ios_agent/
  unreal/
  houdini/
```

Rules:

- One top-level folder per platform.
- Each implemented adapter keeps its own package metadata, runtime entrypoints, tests, README, and AGENT manual inside that platform folder.
- `docs/framework/` stays platform-neutral and documents only the shared protocol and architectural rules.
- Placeholder top-level platform folders may exist before implementation starts so the monorepo structure stays stable as adapters are added.

## Priority model

Priority combines:

- user value for AI-assisted development
- maturity of official host APIs
- fit with the shared discovery-first protocol
- implementation feasibility without relying on brittle UI automation

## Current roadmap

| Priority | Platform | Why it ranks here | Official host interfaces | Status |
|---|---|---|---|---|
| P0 | Unity | High value, rich editor/runtime state, already proven by this repository | Unity Editor API, AssetDatabase, scene/prefab tooling | Implemented |
| P0 | Browser | Extremely high value for frontend/runtime debugging and inspection | Chrome DevTools Protocol, remote debugging HTTP endpoints | Implemented in this branch |
| P1 | Flutter | Strong value for app UI and project tooling, first adapter already in place | Flutter CLI, Dart runtime, DevTools/Inspector ecosystem | Implemented in this branch |
| P1 | Blender | Rich scene/object/material graph and strong official Python API | Blender command line, `bpy`, `bpy.ops`, render API | Implemented in this branch |
| P1 | Houdini | Node graph and procedural pipeline are ideal for host agents | HOM `hou`, `hython`, Python panels | Implemented in this branch |
| P1 | Unreal | Strong editor automation value and mature official scripting surface | Unreal Python API, editor automation, Remote Control API | Implemented in this branch |
| P1 | Figma | High-value design system and file graph workflows with official remote APIs | Figma REST API, comments, variables, file/node/image endpoints | Implemented in this branch |
| P1 | Godot | Good editor plugin surface and scene/resource workflows | `EditorPlugin`, editor scripting, headless CLI | Implemented in this branch |
| P1 | Maya | High-value DCC workflows with mature scripting | Maya Python API, Script Editor, `mayapy` | Implemented in this branch |
| P2 | Android | Valuable host state through SDK, device, and build tooling | ADB, Gradle, Emulator tools | Implemented in this branch |
| P2 | WPF | Valuable desktop UI/runtime tree and XAML workflows | `.sln`, XAML, `dotnet`, `msbuild` | Implemented in this branch |
| P2 | iOS | High value with official but more constrained toolchain surfaces | `xcodebuild`, `xcrun simctl`, Simulator logs | Implemented in this branch |

## Implementation policy

- Only platforms with stable official interfaces should be implemented as first-class adapters.
- Each adapter must expose the shared protocol:
  - `GET /health`
  - `GET /manifest`
  - `GET /manifest/full`
  - `POST /manifest/search`
  - `GET /manifest/bundles`
  - `GET /manifest/bundle/{id}`
  - `POST /tool/describe_many`
  - `POST /call/{toolId}`
  - `GET /result/{handleId}`
  - `GET /agent/brief`
  - `GET /agent`
- Host-specific write or side-effect tools must clearly advertise their risk and use the adapter's access guard.
- Runtime validation should be done against the actual host application when it is available, but lack of the host on one workstation must not block adapter implementation.

## Validation status

- Browser has been runtime-validated in this environment against a locally launched Chrome instance.
- Blender, Houdini, Unreal, Godot, Maya, Figma, Android, WPF, and iOS adapters have passed syntax checks plus local service `/health` validation in this branch.
- Host-runtime tool execution for Blender, Houdini, Unreal, Godot, Maya, Android, WPF, and iOS still depends on those host applications or SDKs being present on the target machine.
