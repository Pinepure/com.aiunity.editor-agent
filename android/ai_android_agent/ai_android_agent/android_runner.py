from __future__ import annotations

import os
import re
import shutil
import subprocess
from pathlib import Path
from typing import Any

from .models import AiAndroidAgentConfig


def probe_android_installation(config: AiAndroidAgentConfig) -> dict[str, Any]:
    project_dir = _resolve_project_dir(config.root_dir, config.project_dir) if config.project_dir else None
    adb_resolved = _resolve_executable(config.adb_executable)
    emulator_name = _configured_emulator_executable(config)
    emulator_resolved = _resolve_executable(emulator_name)
    gradle_command = resolve_gradle_command(config, project_dir=project_dir)
    return {
        "configuredAdbExecutable": config.adb_executable,
        "resolvedAdbExecutable": adb_resolved,
        "configuredGradleExecutable": config.gradle_executable,
        "resolvedGradleCommand": gradle_command,
        "configuredEmulatorExecutable": emulator_name,
        "resolvedEmulatorExecutable": emulator_resolved,
        "androidSdkRoot": _sdk_root(config),
        "projectDir": str(project_dir) if project_dir else None,
        "projectDirExists": bool(project_dir and project_dir.exists()),
        "gradleWrapperPresent": bool(project_dir and (project_dir / "gradlew").exists()),
        "adbVersion": _version_output([config.adb_executable, "version"], timeout=config.tool_timeout_seconds),
        "emulatorVersion": _version_output([emulator_name, "-version"], timeout=config.tool_timeout_seconds),
    }


def run_command(command: list[str], *, timeout_seconds: int, cwd: Path | None = None, env: dict[str, str] | None = None, check: bool = True) -> dict[str, Any]:
    completed = subprocess.run(command, capture_output=True, text=True, timeout=timeout_seconds, cwd=str(cwd) if cwd else None, env=env, check=False)
    payload = {"command": command, "cwd": str(cwd) if cwd else None, "exitCode": completed.returncode, "stdout": completed.stdout, "stderr": completed.stderr}
    if check and completed.returncode != 0:
        raise RuntimeError(f"Command failed with exit code {completed.returncode}: {' '.join(command)}\n{completed.stderr.strip()}")
    return payload


def start_background_process(command: list[str], *, cwd: Path | None = None, env: dict[str, str] | None = None) -> dict[str, Any]:
    process = subprocess.Popen(command, cwd=str(cwd) if cwd else None, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)  # noqa: S603
    return {"command": command, "cwd": str(cwd) if cwd else None, "pid": process.pid}


def resolve_gradle_command(config: AiAndroidAgentConfig, *, project_dir: Path | None) -> list[str]:
    if project_dir is not None:
        wrapper = project_dir / "gradlew"
        if wrapper.exists():
            return [str(wrapper)]
    return [config.gradle_executable]


def list_gradle_modules(project_dir: Path) -> list[str]:
    modules: list[str] = []
    for candidate_name in ("settings.gradle", "settings.gradle.kts"):
        candidate = project_dir / candidate_name
        if not candidate.exists():
            continue
        content = candidate.read_text(encoding="utf-8")
        modules.extend(match.group(1) for match in re.finditer(r"""['"](:[^'"]+)['"]""", content))
    return sorted(set(modules))


def resolve_project_dir(root_dir: Path, value: str) -> Path:
    path = _resolve_project_dir(root_dir, value)
    if not path.exists():
        raise FileNotFoundError(f"Project directory does not exist: {path}")
    return path


def resolve_existing_file(root_dir: Path, value: str) -> Path:
    path = Path(value).expanduser()
    resolved = path.resolve() if path.is_absolute() else (root_dir / path).resolve()
    if not resolved.exists():
        raise FileNotFoundError(f"File does not exist: {resolved}")
    return resolved


def parse_adb_devices(output: str) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for raw_line in output.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("List of devices"):
            continue
        parts = line.split()
        if len(parts) < 2:
            continue
        device = {"serial": parts[0], "state": parts[1]}
        for token in parts[2:]:
            if ":" in token:
                key, value = token.split(":", 1)
                device[key] = value
        result.append(device)
    return result


def parse_package_list(output: str, *, contains: str = "") -> list[str]:
    packages: list[str] = []
    contains = contains.strip()
    for raw_line in output.splitlines():
        line = raw_line.strip()
        if not line.startswith("package:"):
            continue
        package_name = line[len("package:") :]
        if contains and contains not in package_name:
            continue
        packages.append(package_name)
    return packages


def _version_output(command: list[str], *, timeout: int) -> str | None:
    try:
        result = run_command(command, timeout_seconds=timeout, check=False)
        text = (result["stdout"] or result["stderr"]).strip()
        return text or None
    except Exception:
        return None


def _resolve_project_dir(root_dir: Path, value: str) -> Path:
    path = Path(value).expanduser()
    return path.resolve() if path.is_absolute() else (root_dir / path).resolve()


def _sdk_root(config: AiAndroidAgentConfig) -> str:
    configured = config.android_sdk_root.strip()
    if configured:
        return configured
    return os.environ.get("ANDROID_SDK_ROOT", "").strip() or os.environ.get("ANDROID_HOME", "").strip()


def _configured_emulator_executable(config: AiAndroidAgentConfig) -> str:
    configured = config.emulator_executable.strip()
    if configured:
        return configured
    sdk_root = _sdk_root(config)
    if sdk_root:
        candidate = Path(sdk_root) / "emulator" / "emulator"
        return str(candidate)
    return "emulator"


def _resolve_executable(value: str) -> str | None:
    return shutil.which(value) if "/" not in value else (value if Path(value).exists() else None)
