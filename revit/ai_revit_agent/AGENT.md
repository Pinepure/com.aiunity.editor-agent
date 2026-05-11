# Revit Adapter Notes

- Start with `GET /health` and reuse cached capabilities while `manifestHash` stays unchanged.
- Use document, view, element, and selection tools to inspect the live BIM model instead of inferring state from exported files.
- All Revit API work is dispatched through `ExternalEvent`; do not bypass that contract for future write tools.
- Treat `revit.parameter_set` as a host mutation and keep it behind the full-access guard.
- If later you add family-edit, sheet, or linked-model workflows, keep them in separate bundles instead of overloading the core document tools.
