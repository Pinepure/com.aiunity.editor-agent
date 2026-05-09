from __future__ import annotations

import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

from .models import AiWpfAgentConfig

XAML_NAMESPACE = "http://schemas.microsoft.com/winfx/2006/xaml"


def probe_wpf_installation(config: AiWpfAgentConfig) -> dict[str, Any]:
    return {
        "configuredDotnetExecutable": config.dotnet_executable,
        "resolvedDotnetExecutable": _resolve_executable(config.dotnet_executable),
        "configuredMsbuildExecutable": config.msbuild_executable,
        "resolvedMsbuildExecutable": _resolve_executable(config.msbuild_executable),
        "solutionPath": str(resolve_solution_path(config.root_dir, config.solution_path)) if config.solution_path else None,
        "projectDir": str(resolve_project_dir(config.root_dir, config.project_dir)) if config.project_dir else None,
        "dotnetVersion": _version_output([config.dotnet_executable, "--version"], timeout=config.tool_timeout_seconds),
        "msbuildVersion": _version_output([config.msbuild_executable, "-version"], timeout=config.tool_timeout_seconds),
    }


def run_command(command: list[str], *, timeout_seconds: int, cwd: Path | None = None, check: bool = True) -> dict[str, Any]:
    completed = subprocess.run(command, capture_output=True, text=True, timeout=timeout_seconds, cwd=str(cwd) if cwd else None, check=False)
    payload = {"command": command, "cwd": str(cwd) if cwd else None, "exitCode": completed.returncode, "stdout": completed.stdout, "stderr": completed.stderr}
    if check and completed.returncode != 0:
        raise RuntimeError(f"Command failed with exit code {completed.returncode}: {' '.join(command)}\n{completed.stderr.strip()}")
    return payload


def resolve_solution_path(root_dir: Path, value: str) -> Path:
    path = _resolve_path(root_dir, value)
    if path.is_dir():
        matches = sorted(path.glob("*.sln"))
        if not matches:
            raise FileNotFoundError(f"No .sln file found under: {path}")
        return matches[0]
    if not path.exists():
        raise FileNotFoundError(f"Solution path does not exist: {path}")
    return path


def resolve_project_dir(root_dir: Path, value: str) -> Path:
    path = _resolve_path(root_dir, value)
    if not path.exists():
        raise FileNotFoundError(f"Project directory does not exist: {path}")
    return path


def list_solution_projects(solution_path: Path) -> list[dict[str, Any]]:
    content = solution_path.read_text(encoding="utf-8", errors="replace")
    projects: list[dict[str, Any]] = []
    pattern = re.compile(r'^Project\("\{[^"]+\}"\)\s*=\s*"([^"]+)",\s*"([^"]+)",\s*"\{([^"]+)\}"', re.MULTILINE)
    for match in pattern.finditer(content):
        name, relative_path, guid = match.groups()
        projects.append({"name": name, "relativePath": relative_path.replace("\\", "/"), "guid": guid})
    return projects


def list_files(root: Path, *, suffixes: tuple[str, ...]) -> list[Path]:
    result: list[Path] = []
    for path in root.rglob("*"):
        if not path.is_file():
            continue
        if any(part in {"bin", "obj", ".git"} for part in path.parts):
            continue
        if path.suffix.lower() in suffixes:
            result.append(path)
    return sorted(result)


def search_text(root: Path, *, query: str, suffixes: tuple[str, ...], max_results: int) -> list[dict[str, Any]]:
    lowered = query.lower()
    results: list[dict[str, Any]] = []
    for path in list_files(root, suffixes=suffixes):
        for index, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), start=1):
            if lowered not in line.lower():
                continue
            results.append({"path": str(path), "line": index, "text": line.strip()})
            if len(results) >= max_results:
                return results
    return results


def list_resource_keys(xaml_path: Path) -> list[str]:
    content = xaml_path.read_text(encoding="utf-8", errors="replace")
    return sorted(set(match.group(1) for match in re.finditer(r'(?:x:Key|Key)\s*=\s*"([^"]+)"', content)))


def set_xaml_attribute(*, xaml_path: Path, element_name: str, attribute_name: str, value: str, output_path: Path) -> dict[str, Any]:
    tree = ET.parse(xaml_path)
    root = tree.getroot()
    target = _find_named_element(root, element_name)
    if target is None:
        raise RuntimeError(f"Element not found by Name or x:Name: {element_name}")
    target.set(attribute_name, value)
    tree.write(output_path, encoding="utf-8", xml_declaration=True)
    return {"xamlPath": str(xaml_path), "outputPath": str(output_path), "elementName": element_name, "attributeName": attribute_name, "value": value}


def _find_named_element(root: ET.Element, element_name: str) -> ET.Element | None:
    x_name = f"{{{XAML_NAMESPACE}}}Name"
    for element in root.iter():
        if element.get(x_name) == element_name or element.get("Name") == element_name:
            return element
    return None


def _version_output(command: list[str], *, timeout: int) -> str | None:
    try:
        result = run_command(command, timeout_seconds=timeout, check=False)
        text = (result["stdout"] or result["stderr"]).strip()
        return text or None
    except Exception:
        return None


def _resolve_executable(value: str) -> str | None:
    return shutil.which(value) if "/" not in value else (value if Path(value).exists() else None)


def _resolve_path(root_dir: Path, value: str) -> Path:
    path = Path(value).expanduser()
    return path.resolve() if path.is_absolute() else (root_dir / path).resolve()
