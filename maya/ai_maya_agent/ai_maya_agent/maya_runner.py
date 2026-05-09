from __future__ import annotations

import json
import subprocess
import tempfile
from pathlib import Path
from typing import Any

from .models import AiMayaAgentConfig


def probe_maya_version(mayapy_executable: str) -> dict[str, Any]:
    script = (
        "import json; import maya.standalone; maya.standalone.initialize(name='python'); "
        "import maya.cmds as cmds; print(json.dumps({'version': cmds.about(version=True), 'apiVersion': cmds.about(apiVersion=True)}, ensure_ascii=True))"
    )
    completed = subprocess.run([mayapy_executable, "-c", script], capture_output=True, text=True, check=False)
    payload = _parse_last_json_line(completed.stdout)
    return {"executable": mayapy_executable, "exitCode": completed.returncode, "stdout": completed.stdout.strip(), "stderr": completed.stderr.strip(), "version": payload}


def run_maya_operation(
    config: AiMayaAgentConfig,
    *,
    operation: str,
    args: dict[str, Any],
    scene_file: Path | None,
) -> dict[str, Any]:
    bridge_path = Path(__file__).resolve().with_name("maya_bridge.py")
    with tempfile.TemporaryDirectory(prefix="ai_maya_agent_") as temp_dir:
        temp_root = Path(temp_dir)
        payload_path = temp_root / "payload.json"
        output_path = temp_root / "output.json"
        payload_path.write_text(json.dumps({"operation": operation, "args": args, "sceneFile": str(scene_file) if scene_file else ""}, indent=2, ensure_ascii=True), encoding="utf-8")
        command = [config.mayapy_executable, str(bridge_path), str(payload_path), str(output_path)]
        completed = subprocess.run(command, capture_output=True, text=True, timeout=config.tool_timeout_seconds, check=False)
        if not output_path.exists():
            raise RuntimeError(f"Maya did not produce an output payload. Exit code: {completed.returncode}. stderr: {completed.stderr.strip()}")
        payload = json.loads(output_path.read_text(encoding="utf-8"))
        if completed.returncode != 0:
            raise RuntimeError(f"Maya exited with code {completed.returncode}. stderr: {completed.stderr.strip()}")
        if not payload.get("ok", False):
            raise RuntimeError(f"Maya operation {operation} failed: {payload.get('error', 'unknown error')}\n{payload.get('traceback', '')}".strip())
        return {"command": command, "stdout": completed.stdout.strip(), "stderr": completed.stderr.strip(), "result": payload.get("result")}


def _parse_last_json_line(stdout: str) -> dict[str, Any]:
    for line in reversed([item.strip() for item in stdout.splitlines() if item.strip()]):
        try:
            parsed = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(parsed, dict):
            return parsed
    return {}
