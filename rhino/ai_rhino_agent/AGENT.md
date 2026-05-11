# Rhino Adapter Notes

- Start with `GET /health` and reuse cached capabilities while `manifestHash` stays unchanged.
- Prefer `POST /manifest/search` or focused bundles before loading full schemas.
- Use document, layer, object, selection, and viewport tools to inspect Rhino state instead of inferring it from exported files.
- Treat `rhino.command_run` and `rhino.layer_visibility_set` as host mutations; they are intentionally gated.
- If Grasshopper-specific visibility is needed later, add it through a separate `rhino.grasshopper_*` bundle instead of overloading core Rhino tools.
