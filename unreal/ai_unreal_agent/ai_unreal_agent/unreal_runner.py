from __future__ import annotations

import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path
from typing import Any

from .models import AiUnrealAgentConfig


def probe_unreal_installation(config: AiUnrealAgentConfig) -> dict[str, Any]:
    resolved = shutil.which(config.unreal_executable) if "/" not in config.unreal_executable else config.unreal_executable
    project_file = Path(config.project_file).expanduser().resolve() if config.project_file else None
    return {
        "configuredExecutable": config.unreal_executable,
        "resolvedExecutable": resolved,
        "executableExists": bool(resolved and ("/" not in resolved or Path(resolved).exists())),
        "projectFile": str(project_file) if project_file else None,
        "projectFileExists": bool(project_file and project_file.exists()),
        "remoteControlUrl": config.remote_control_url,
    }


def run_unreal_operation(
    config: AiUnrealAgentConfig,
    *,
    operation: str,
    args: dict[str, Any],
    project_file: Path,
) -> dict[str, Any]:
    bridge_path = Path(__file__).resolve().with_name("unreal_bridge.py")
    with tempfile.TemporaryDirectory(prefix="ai_unreal_agent_") as temp_dir:
        temp_root = Path(temp_dir)
        payload_path = temp_root / "payload.json"
        output_path = temp_root / "output.json"
        payload_path.write_text(
            json.dumps({"operation": operation, "args": args}, indent=2, ensure_ascii=True),
            encoding="utf-8",
        )
        env = os.environ.copy()
        env["AI_UNREAL_AGENT_PAYLOAD"] = str(payload_path)
        env["AI_UNREAL_AGENT_OUTPUT"] = str(output_path)
        command = [
            config.unreal_executable,
            str(project_file),
            "-Unattended",
            "-NoSplash",
            "-NullRHI",
            f"-ExecutePythonScript={bridge_path}",
        ]
        completed = subprocess.run(
            command,
            capture_output=True,
            text=True,
            timeout=config.tool_timeout_seconds,
            env=env,
            check=False,
        )
        if not output_path.exists():
            raise RuntimeError(
                f"Unreal did not produce an output payload. Exit code: {completed.returncode}. "
                f"stderr: {completed.stderr.strip()}"
            )
        payload = json.loads(output_path.read_text(encoding="utf-8"))
        if completed.returncode != 0:
            raise RuntimeError(
                f"Unreal exited with code {completed.returncode}. stderr: {completed.stderr.strip()}"
            )
        if not payload.get("ok", False):
            raise RuntimeError(
                f"Unreal operation {operation} failed: {payload.get('error', 'unknown error')}\n"
                f"{payload.get('traceback', '')}".strip()
            )
        return {
            "command": command,
            "stdout": completed.stdout.strip(),
            "stderr": completed.stderr.strip(),
            "result": payload.get("result"),
        }
