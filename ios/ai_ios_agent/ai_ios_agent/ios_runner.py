from __future__ import annotations

import json
import shutil
import subprocess
from pathlib import Path
from typing import Any

from .models import AiIosAgentConfig


def probe_ios_installation(config: AiIosAgentConfig) -> dict[str, Any]:
    return {
        "configuredXcodebuildExecutable": config.xcodebuild_executable,
        "resolvedXcodebuildExecutable": _resolve_executable(config.xcodebuild_executable),
        "configuredXcrunExecutable": config.xcrun_executable,
        "resolvedXcrunExecutable": _resolve_executable(config.xcrun_executable),
        "projectPath": str(resolve_optional_path(config.root_dir, config.project_path)) if config.project_path else None,
        "workspacePath": str(resolve_optional_path(config.root_dir, config.workspace_path)) if config.workspace_path else None,
        "scheme": config.scheme or None,
        "destination": config.destination,
        "xcodeVersion": _version_output([config.xcodebuild_executable, "-version"], timeout=config.tool_timeout_seconds),
        "simctlPath": _version_output([config.xcrun_executable, "--find", "simctl"], timeout=config.tool_timeout_seconds),
    }


def run_command(command: list[str], *, timeout_seconds: int, cwd: Path | None = None, check: bool = True) -> dict[str, Any]:
    completed = subprocess.run(command, capture_output=True, text=True, timeout=timeout_seconds, cwd=str(cwd) if cwd else None, check=False)
    payload = {"command": command, "cwd": str(cwd) if cwd else None, "exitCode": completed.returncode, "stdout": completed.stdout, "stderr": completed.stderr}
    if check and completed.returncode != 0:
        raise RuntimeError(f"Command failed with exit code {completed.returncode}: {' '.join(command)}\n{completed.stderr.strip()}")
    return payload


def simctl_json(config: AiIosAgentConfig, *subcommand: str) -> Any:
    result = run_command([config.xcrun_executable, "simctl", *subcommand], timeout_seconds=config.tool_timeout_seconds)
    return json.loads(result["stdout"])


def resolve_optional_path(root_dir: Path, value: str) -> Path:
    path = Path(value).expanduser()
    resolved = path.resolve() if path.is_absolute() else (root_dir / path).resolve()
    if not resolved.exists():
        raise FileNotFoundError(f"Path does not exist: {resolved}")
    return resolved


def build_xcode_context_args(*, project_path: Path | None, workspace_path: Path | None, scheme: str) -> list[str]:
    command: list[str] = []
    if workspace_path is not None:
        command.extend(["-workspace", str(workspace_path)])
    elif project_path is not None:
        command.extend(["-project", str(project_path)])
    if scheme:
        command.extend(["-scheme", scheme])
    return command


def _version_output(command: list[str], *, timeout: int) -> str | None:
    try:
        result = run_command(command, timeout_seconds=timeout, check=False)
        text = (result["stdout"] or result["stderr"]).strip()
        return text or None
    except Exception:
        return None


def _resolve_executable(value: str) -> str | None:
    return shutil.which(value) if "/" not in value else (value if Path(value).exists() else None)
