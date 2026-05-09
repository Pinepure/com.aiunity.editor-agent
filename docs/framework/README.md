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
- `docs/framework/`
  Shared protocol and architecture docs.

## Adapter model

Each adapter owns the host-specific work:

- Unity owns editor state, assets, scenes, prefabs, compile state, generated editor tools, and confirmation dialogs.
- Flutter owns project inspection, source search, file-safe mutations, Flutter CLI workflows, and widget indexing.

They share the protocol shape, discovery flow, manifest conventions, result paging conventions, and AI workflow guidance.
