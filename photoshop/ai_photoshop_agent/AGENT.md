# Photoshop Adapter Notes

- Start with `GET /health` and reuse cached capabilities while `manifestHash` stays unchanged.
- The Python service only exposes the shared protocol; actual host work runs inside the UXP plugin.
- Verify the bridge heartbeat before assuming Photoshop is reachable.
- Use document and layer inspection before any mutation.
- Treat `photoshop.layer_visibility_set`, `photoshop.text_layer_set`, and `photoshop.document_save` as host mutations behind the full-access guard.
- This adapter supports dynamic tool registration through `tool.get_template`, `tool.upsert_generated`, `tool.list_generated`, `tool.delete_generated`, and `tool.reload_generated`.
- Generated Photoshop bridge tool definitions live under `<bridge-dir>/generated_tools/`.
- Generated tool source runs inside the UXP plugin as async JavaScript with `(args, host, require, console)`.
