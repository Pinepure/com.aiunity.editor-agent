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
  vscode/ai_vscode_agent/
  jetbrains/ai_jetbrains_agent/
  figma/ai_figma_agent/
  godot/ai_godot_agent/
  maya/ai_maya_agent/
  cocos/
  rhino/ai_rhino_agent/
  revit/ai_revit_agent/
  photoshop/ai_photoshop_agent/
  android/ai_android_agent/
  wpf/ai_wpf_agent/
  ios/ai_ios_agent/
  unreal/ai_unreal_agent/
  houdini/ai_houdini_agent/
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

| Priority | Platform | Why it ranks here | Official host interfaces | Dynamic tool status | Status |
|---|---|---|---|---|---|
| P0 | Unity | High value, rich editor/runtime state, already proven by this repository | Unity Editor API, AssetDatabase, scene/prefab tooling | Dynamic generated tools implemented | Implemented |
| P0 | Browser | Extremely high value for frontend/runtime debugging and inspection | Chrome DevTools Protocol, remote debugging HTTP endpoints | Static toolset | Implemented in this branch |
| P1 | Flutter | Strong value for app UI and project tooling, first adapter already in place | Flutter CLI, Dart runtime, DevTools/Inspector ecosystem | Dynamic generated tools implemented | Implemented in this branch |
| P1 | Blender | Rich scene/object/material graph and strong official Python API | Blender command line, `bpy`, `bpy.ops`, render API | Static toolset | Implemented in this branch |
| P1 | Houdini | Node graph and procedural pipeline are ideal for host agents | HOM `hou`, `hython`, Python panels | Static toolset | Implemented in this branch |
| P1 | Unreal | Strong editor automation value and mature official scripting surface | Unreal Python API, editor automation, Remote Control API | Planned next batch | Implemented in this branch |
| P1 | Figma | High-value design system and file graph workflows with official remote APIs | Figma REST API, comments, variables, file/node/image endpoints | Static toolset | Implemented in this branch |
| P0 | VS Code | Broadest IDE reach, strong official host APIs, and high leverage for code workflows | VS Code Extension API, language services, tasks, commands | Dynamic generated tools implemented | Implemented in this branch |
| P0 | JetBrains | High-value IDE context and mature PSI/project model across multiple products | IntelliJ Platform SDK, PSI, project model, run configurations | Dynamic generated tools implemented | Implemented in this branch |
| P1 | Godot | Good editor plugin surface and scene/resource workflows | `EditorPlugin`, editor scripting, headless CLI | Planned next batch | Implemented in this branch |
| P1 | Maya | High-value DCC workflows with mature scripting | Maya Python API, Script Editor, `mayapy` | Static toolset | Implemented in this branch |
| P1 | Rhino | Valuable CAD and parametric modeling context with official cross-platform plugin SDK | RhinoCommon, Rhino plugin load model, Rhino document APIs | Static toolset | Implemented in this branch |
| P1 | Photoshop | High-value creative workflows with modern official plugin APIs | Photoshop UXP, Photoshop DOM, `batchPlay`, UXP file APIs | Dynamic generated tools implemented | Implemented in this branch |
| P1 | Cocos Creator | Editor tooling can support a generated script host if a stable extension dispatcher owns the runtime | Cocos Creator extensions, messages, asset database, reload flow | Planned next batch | Planned |
| P2 | Android | Valuable host state through SDK, device, and build tooling | ADB, Gradle, Emulator tools | Static toolset | Implemented in this branch |
| P2 | Revit | Very high BIM value with strong but main-thread-constrained official APIs | Revit API, `.addin` manifests, `ExternalEvent` | Static toolset | Implemented in this branch |
| P2 | WPF | Valuable desktop UI/runtime tree and XAML workflows | `.sln`, XAML, `dotnet`, `msbuild` | Static toolset | Implemented in this branch |
| P2 | iOS | High value with official but more constrained toolchain surfaces | `xcodebuild`, `xcrun simctl`, Simulator logs | Static toolset | Implemented in this branch |

## Dynamic tool rollout

The repository now has two adapter categories:

- Dynamic adapters: `Unity`, `Flutter`, `VS Code`, `JetBrains`, and `Photoshop`
- Static adapters: every other currently implemented platform

The next dynamic batch is prioritized as:

1. `Unreal`
2. `Godot`
3. `Cocos Creator`

Implementation rules for that batch:

- `Unreal` should generate Python tool files under the project-owned state directory and execute them through the official Unreal Python surface.
- `Godot` should generate GDScript tools inside the adapter-owned addon/runtime area and dispatch them through a stable editor/headless bridge.
- `Cocos Creator` should not generate whole new extensions; it should own one stable extension host that dispatches generated JavaScript tools from a managed directory.

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
- VS Code extension source and Photoshop companion plus UXP plugin source have passed syntax checks in this environment.
- Blender, Houdini, Unreal, Godot, Maya, Figma, Android, WPF, and iOS adapters have passed syntax checks plus local service `/health` validation in this branch.
- JetBrains, Rhino, and Revit adapters have source implementations in this branch but were not built here because this workstation lacks the required IDE or .NET/Revit host SDK runtime.
- Host-runtime tool execution for Blender, Houdini, Unreal, Godot, Maya, Android, WPF, iOS, Rhino, Revit, Photoshop, VS Code, and JetBrains still depends on those host applications or SDKs being present on the target machine.
