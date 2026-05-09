from __future__ import annotations

import json
import subprocess
import tempfile
from pathlib import Path
from typing import Any

from .models import AiGodotAgentConfig


def probe_godot_version(godot_executable: str) -> dict[str, Any]:
    completed = subprocess.run([godot_executable, "--version"], capture_output=True, text=True, check=False)
    return {
        "executable": godot_executable,
        "exitCode": completed.returncode,
        "stdout": completed.stdout.strip(),
        "stderr": completed.stderr.strip(),
        "versionLine": next((line.strip() for line in completed.stdout.splitlines() if line.strip()), ""),
    }


def run_godot_operation(
    config: AiGodotAgentConfig,
    *,
    operation: str,
    args: dict[str, Any],
    project_dir: Path,
) -> dict[str, Any]:
    bridge_path = Path(__file__).resolve().with_name("godot_bridge.gd")
    with tempfile.TemporaryDirectory(prefix="ai_godot_agent_") as temp_dir:
        temp_root = Path(temp_dir)
        payload_path = temp_root / "payload.json"
        output_path = temp_root / "output.json"
        payload_path.write_text(json.dumps({"operation": operation, "args": args}, indent=2, ensure_ascii=True), encoding="utf-8")
        command = [
            config.godot_executable,
            "--headless",
            "--path",
            str(project_dir),
            "--script",
            str(bridge_path),
            "--",
            str(payload_path),
            str(output_path),
        ]
        completed = subprocess.run(command, capture_output=True, text=True, timeout=config.tool_timeout_seconds, check=False)
        if not output_path.exists():
            raise RuntimeError(
                f"Godot did not produce an output payload. Exit code: {completed.returncode}. "
                f"stderr: {completed.stderr.strip()}"
            )
        payload = json.loads(output_path.read_text(encoding="utf-8"))
        if completed.returncode != 0:
            raise RuntimeError(f"Godot exited with code {completed.returncode}. stderr: {completed.stderr.strip()}")
        if not payload.get("ok", False):
            raise RuntimeError(
                f"Godot operation {operation} failed: {payload.get('error', 'unknown error')}\n"
                f"{payload.get('traceback', '')}".strip()
            )
        return {"command": command, "stdout": completed.stdout.strip(), "stderr": completed.stderr.strip(), "result": payload.get("result")}
