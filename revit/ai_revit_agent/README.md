# AI Revit Agent

AI Revit Agent is the Revit adapter for the shared AI Platform Agent Framework.

It runs inside Revit as an `IExternalApplication` add-in and exposes the shared discovery-first protocol over a local HTTP listener.

## What the first Revit adapter covers

- Revit startup registration through a `.addin` manifest
- Main-thread-safe API dispatch through `ExternalEvent`
- Application and active-document summary inspection
- Open document, view, element, and selection inspection
- Element detail inspection by id
- Controlled parameter mutation inside a Revit transaction
- Paged service logs and recent tool calls

## Build

Set the Revit API DLL location before building:

```powershell
$env:REVIT_API_DLL_DIR="C:\Program Files\Autodesk\Revit 2025"
dotnet build revit/ai_revit_agent/AiRevitAgent.csproj
```

## Install

1. Build `AiRevitAgent.dll`.
2. Copy and edit `AiRevitAgent.addin.template` to a real `.addin` file.
3. Place it in one of Revit's add-in manifest folders.
4. Point the `<Assembly>` field to the built DLL path.

## Runtime state

The add-in stores state under:

```text
%APPDATA%\AIPlatformAgent\revit\
```

Useful files:

- `config.json`
- `token.txt`

`config.json` can override:

```json
{
  "host": "127.0.0.1",
  "port": 19793,
  "requireToken": true,
  "fullAccessEnabled": false,
  "toolTimeoutMs": 120000
}
```
