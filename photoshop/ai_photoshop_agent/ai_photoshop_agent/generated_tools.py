from __future__ import annotations

import json
from pathlib import Path

from .models import AiToolDefinition, schema_json


class PhotoshopGeneratedToolHost:
    def __init__(self, *, config, bridge_client) -> None:
        self.config = config
        self.bridge_client = bridge_client

    @property
    def definitions_dir(self) -> Path:
        return self.config.generated_tools_directory_path

    def ensure_layout(self) -> None:
        self.bridge_client.ensure_layout()
        self.definitions_dir.mkdir(parents=True, exist_ok=True)

    def compute_fingerprint(self) -> str:
        self.ensure_layout()
        parts: list[str] = []
        for file_name in self._list_files():
            parts.append(f"{file_name}\n{(self.definitions_dir / file_name).read_text(encoding='utf-8')}")
        return "\n---\n".join(parts)

    def load_definitions(self) -> list[dict[str, object]]:
        self.ensure_layout()
        return [{**self.read_definition(file_name), "fileName": file_name} for file_name in self._list_files()]

    def list_definitions(self) -> list[dict[str, object]]:
        return [
            {
                "fileName": definition["fileName"],
                "toolId": definition["toolId"],
                "description": definition["description"],
                "danger": definition["danger"],
                "requiresConfirmation": bool(definition["requiresConfirmation"]),
            }
            for definition in self.load_definitions()
        ]

    def get_template(self, *, tool_id: str = "generated.photoshop_example", description: str = "Generated Photoshop tool.", file_name: str = "") -> dict[str, object]:
        return {
            "fileName": normalize_generated_tool_file_name(file_name or suggested_file_name(tool_id)),
            "definition": {
                "toolId": tool_id,
                "description": description,
                "danger": "low",
                "requiresConfirmation": False,
                "argsSchema": {
                    "type": "object",
                    "additionalProperties": False,
                    "properties": {
                        "documentId": {"type": "integer"},
                    },
                },
                "returnSchema": {
                    "type": "object",
                    "additionalProperties": True,
                },
                "source":
"""const document = host.requireActiveDocument(args.documentId);
const layers = host.listLayers(document.id);
return {
  document: host.summarizeDocument(document),
  layerCount: layers.length,
};""",
            },
            "notes": [
                "The source body runs inside the Photoshop UXP plugin as async JavaScript with (args, host, require, console).",
                "Use host.app and host.core for the official Photoshop APIs.",
                "Use host.modal(name, fn) for write operations that need executeAsModal.",
            ],
        }

    def upsert_definition(self, *, file_name: str, definition: object) -> dict[str, object]:
        self.ensure_layout()
        safe_name = normalize_generated_tool_file_name(file_name)
        payload = normalize_generated_tool_definition(definition)
        target_path = self.definitions_dir / safe_name
        target_path.write_text(f"{json.dumps(payload, ensure_ascii=True, indent=2)}\n", encoding="utf-8")
        return {"fileName": safe_name, "path": str(target_path)}

    def delete_definition(self, file_name: str) -> dict[str, object]:
        self.ensure_layout()
        safe_name = normalize_generated_tool_file_name(file_name)
        target_path = self.definitions_dir / safe_name
        if not target_path.exists():
            return {"deleted": False, "fileName": safe_name}
        target_path.unlink()
        return {"deleted": True, "fileName": safe_name}

    def create_tool_definition(self, definition: dict[str, object], context) -> AiToolDefinition:
        return AiToolDefinition(
            id=str(definition["toolId"]),
            description=str(definition["description"]),
            args_schema_json=schema_json(definition["argsSchema"]),
            return_schema_json=schema_json(definition["returnSchema"]),
            handler_name=f"generated:{definition['fileName']}",
            danger=str(definition["danger"]),
            requires_confirmation=bool(definition["requiresConfirmation"]),
            handler=lambda args, ctx: ctx.bridge_client.call(str(definition["toolId"]), args, timeout_seconds=ctx.config.tool_timeout_seconds),
        )

    def read_definition(self, file_name: str) -> dict[str, object]:
        safe_name = normalize_generated_tool_file_name(file_name)
        raw = json.loads((self.definitions_dir / safe_name).read_text(encoding="utf-8"))
        return normalize_generated_tool_definition(raw)

    def _list_files(self) -> list[str]:
        return sorted(entry.name for entry in self.definitions_dir.iterdir() if entry.is_file() and entry.name.endswith(".json"))


def normalize_generated_tool_definition(definition: object) -> dict[str, object]:
    if not isinstance(definition, dict):
        raise RuntimeError("definition must be an object.")
    tool_id = str(definition.get("toolId") or "").strip()
    description = str(definition.get("description") or "").strip()
    source = str(definition.get("source") or "")
    if not tool_id:
        raise RuntimeError("definition.toolId is required.")
    if not description:
        raise RuntimeError("definition.description is required.")
    if not source.strip():
        raise RuntimeError("definition.source is required.")
    return {
        "toolId": tool_id,
        "description": description,
        "danger": str(definition.get("danger") or "low"),
        "requiresConfirmation": bool(definition.get("requiresConfirmation")),
        "argsSchema": ensure_schema_object(definition.get("argsSchema")),
        "returnSchema": ensure_schema_object(definition.get("returnSchema")),
        "source": source,
    }


def normalize_generated_tool_file_name(file_name: str) -> str:
    trimmed = str(file_name or "").strip()
    if not trimmed:
        raise RuntimeError("fileName is required.")
    safe_name = Path(trimmed).name
    if safe_name != trimmed:
        raise RuntimeError("Subdirectories are not allowed. Provide only a file name.")
    if not safe_name.endswith(".json"):
        raise RuntimeError("Generated tool files must use the .json extension.")
    return safe_name


def suggested_file_name(tool_id: str) -> str:
    characters = [char if char.isalnum() or char in "._-" else "_" for char in str(tool_id or "generated.photoshop_example")]
    return "".join(characters) + ".json"


def ensure_schema_object(value: object) -> dict[str, object]:
    return value if isinstance(value, dict) else {"type": "object"}
