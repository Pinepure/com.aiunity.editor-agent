from __future__ import annotations

import json
import subprocess
import tempfile
from pathlib import Path
from typing import Any

from .models import AiBlenderAgentConfig


def probe_blender_version(blender_executable: str) -> dict[str, Any]:
    completed = subprocess.run(
        [blender_executable, "--version"],
        capture_output=True,
        text=True,
        check=False,
    )
    lines = [line.strip() for line in completed.stdout.splitlines() if line.strip()]
    return {
        "executable": blender_executable,
        "exitCode": completed.returncode,
        "stdout": completed.stdout.strip(),
        "stderr": completed.stderr.strip(),
        "versionLine": lines[0] if lines else "",
    }


def run_blender_operation(
    config: AiBlenderAgentConfig,
    *,
    operation: str,
    args: dict[str, Any],
    blend_file: Path | None,
) -> dict[str, Any]:
    bridge_path = Path(__file__).resolve().with_name("blender_bridge.py")
    with tempfile.TemporaryDirectory(prefix="ai_blender_agent_") as temp_dir:
        temp_root = Path(temp_dir)
        payload_path = temp_root / "payload.json"
        output_path = temp_root / "output.json"
        payload_path.write_text(
            json.dumps(
                {
                    "operation": operation,
                    "args": args,
                },
                indent=2,
                ensure_ascii=True,
            ),
            encoding="utf-8",
        )

        command = [config.blender_executable, "--background"]
        if blend_file is not None:
            command.append(str(blend_file))
        else:
            command.append("--factory-startup")
        command.extend(
            [
                "--python",
                str(bridge_path),
                "--",
                str(payload_path),
                str(output_path),
            ]
        )
        completed = subprocess.run(
            command,
            capture_output=True,
            text=True,
            timeout=config.tool_timeout_seconds,
            check=False,
        )
        if not output_path.exists():
            raise RuntimeError(
                f"Blender did not produce an output payload. Exit code: {completed.returncode}. "
                f"stderr: {completed.stderr.strip()}"
            )

        payload = json.loads(output_path.read_text(encoding="utf-8"))
        if completed.returncode != 0:
            raise RuntimeError(
                f"Blender exited with code {completed.returncode}. stderr: {completed.stderr.strip()}"
            )
        if not payload.get("ok", False):
            raise RuntimeError(
                f"Blender operation {operation} failed: {payload.get('error', 'unknown error')}\n"
                f"{payload.get('traceback', '')}".strip()
            )
        return {
            "command": command,
            "stdout": completed.stdout.strip(),
            "stderr": completed.stderr.strip(),
            "result": payload.get("result"),
        }
