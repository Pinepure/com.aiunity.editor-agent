from __future__ import annotations

from dataclasses import dataclass, field
from hashlib import sha256
from pathlib import Path
from typing import Any, Callable

JsonMap = dict[str, Any]
AiToolHandler = Callable[[JsonMap, "AiToolExecutionContext"], Any]


class AiToolDanger:
    LOW = "low"
    MEDIUM = "medium"
    HIGH = "high"


@dataclass(frozen=True)
class AiIosAgentConfig:
    root_dir: Path
    host: str = "127.0.0.1"
    port: int = 19789
    require_token: bool = True
    full_access_enabled: bool = False
    xcodebuild_executable: str = "xcodebuild"
    xcrun_executable: str = "xcrun"
    project_path: str = ""
    workspace_path: str = ""
    scheme: str = ""
    destination: str = "platform=iOS Simulator,name=iPhone 15"
    tool_timeout_seconds: int = 600
    service_version: str = "0.1.0"

    FRAMEWORK_NAME = "AI Platform Agent Framework"
    PROTOCOL_VERSION = "2.0"
    SERVICE_ID = "aiios.agent"
    SERVICE_NAME = "AI iOS Agent"
    PLATFORM_ID = "ios"
    PRIMARY_TOKEN_HEADER = "X-AI-Agent-Token"
    LEGACY_TOKEN_HEADER = "X-Ios-Ai-Token"

    def __post_init__(self) -> None:
        object.__setattr__(self, "root_dir", Path(self.root_dir).expanduser().resolve())

    @property
    def accepted_token_headers(self) -> list[str]:
        return [self.PRIMARY_TOKEN_HEADER, self.LEGACY_TOKEN_HEADER]

    @property
    def server_url(self) -> str:
        return f"http://{self.host}:{self.port}"

    @property
    def state_directory_path(self) -> Path:
        return self.root_dir / ".ai_platform_agent" / "ios"

    @property
    def token_file_path(self) -> Path:
        return self.state_directory_path / "token.txt"


class AiRuntimeState:
    def __init__(self) -> None:
        self._service_logs: list[JsonMap] = []
        self._tool_calls: list[JsonMap] = []

    def log(self, level: str, message: str) -> None:
        self._service_logs.append({"time": iso_timestamp(), "level": level, "message": message})
        if len(self._service_logs) > 400:
            self._service_logs.pop(0)

    def record_call(self, *, tool_id: str, ok: bool, duration_ms: int, message: str) -> None:
        self._tool_calls.append({"time": iso_timestamp(), "toolId": tool_id, "ok": ok, "durationMs": duration_ms, "message": message})
        if len(self._tool_calls) > 400:
            self._tool_calls.pop(0)

    def recent_logs(self, max_entries: int) -> list[JsonMap]:
        safe_max = clamp_number(max_entries or 100, 1, 300)
        return list(self._service_logs[-safe_max:])

    def recent_calls(self, max_entries: int) -> list[JsonMap]:
        safe_max = clamp_number(max_entries or 100, 1, 300)
        return list(self._tool_calls[-safe_max:])


@dataclass(frozen=True)
class AiManifestBundleDefinition:
    id: str
    description: str
    prefixes: list[str]


@dataclass
class AiToolDefinition:
    id: str
    description: str
    args_schema_json: str
    return_schema_json: str
    handler_name: str
    handler: AiToolHandler
    danger: str = AiToolDanger.LOW
    requires_confirmation: bool = False
    namespace_id: str = field(init=False)

    def __post_init__(self) -> None:
        self.namespace_id = derive_namespace_id(self.id)

    def to_summary_json(self) -> JsonMap:
        return {"id": self.id, "namespaceId": self.namespace_id, "description": self.description, "danger": self.danger, "requiresConfirmation": self.requires_confirmation}

    def to_full_json(self) -> JsonMap:
        return {"id": self.id, "namespaceId": self.namespace_id, "description": self.description, "argsSchemaJson": self.args_schema_json, "returnSchemaJson": self.return_schema_json, "danger": self.danger, "requiresConfirmation": self.requires_confirmation, "handlerName": self.handler_name}


@dataclass(frozen=True)
class AiToolExecutionContext:
    config: AiIosAgentConfig
    registry: "AiToolRegistry"
    result_handle_store: Any
    runtime_state: AiRuntimeState
    agent_manual: str
    read_token: Callable[[], str]
    regenerate_token: Callable[[], str]
    build_health_payload: Callable[..., JsonMap]
    build_agent_brief_payload: Callable[..., JsonMap]


class AiToolRegistry:
    def __init__(self, *, bundles: list[AiManifestBundleDefinition]) -> None:
        self._bundles = bundles
        self._tools: dict[str, AiToolDefinition] = {}

    def register(self, tool: AiToolDefinition) -> None:
        if tool.id in self._tools:
            raise RuntimeError(f"Duplicate tool id: {tool.id}")
        self._tools[tool.id] = tool

    def find_tool(self, tool_id: str) -> AiToolDefinition | None:
        return self._tools.get(tool_id)

    @property
    def count(self) -> int:
        return len(self._tools)

    @property
    def tools(self) -> list[AiToolDefinition]:
        return sorted(self._tools.values(), key=lambda item: item.id)

    @property
    def namespace_infos(self) -> list[JsonMap]:
        counts: dict[str, int] = {}
        for tool in self._tools.values():
            counts[tool.namespace_id] = counts.get(tool.namespace_id, 0) + 1
        return [{"id": key, "count": value} for key, value in sorted(counts.items(), key=lambda item: item[0])]

    @property
    def manifest_hash(self) -> str:
        digest = sha256()
        digest.update(AiIosAgentConfig.PROTOCOL_VERSION.encode("utf-8"))
        for tool in self.tools:
            digest.update(tool.id.encode("utf-8"))
            digest.update(tool.namespace_id.encode("utf-8"))
            digest.update(tool.description.encode("utf-8"))
            digest.update(tool.args_schema_json.encode("utf-8"))
            digest.update(tool.return_schema_json.encode("utf-8"))
            digest.update(tool.danger.encode("utf-8"))
            digest.update(b"1" if tool.requires_confirmation else b"0")
            digest.update(tool.handler_name.encode("utf-8"))
        return digest.hexdigest()

    def build_manifest_summary(self, config: AiIosAgentConfig) -> JsonMap:
        return {"ok": True, "framework": AiIosAgentConfig.FRAMEWORK_NAME, "serviceId": AiIosAgentConfig.SERVICE_ID, "service": AiIosAgentConfig.SERVICE_NAME, "platformId": AiIosAgentConfig.PLATFORM_ID, "version": config.service_version, "protocolVersion": AiIosAgentConfig.PROTOCOL_VERSION, "manifestHash": self.manifest_hash, "toolCount": self.count, "namespaces": self.namespace_infos, "tools": [tool.to_summary_json() for tool in self.tools]}

    def build_manifest_full(self, config: AiIosAgentConfig) -> JsonMap:
        payload = self.build_manifest_summary(config)
        payload["tools"] = [tool.to_full_json() for tool in self.tools]
        return payload

    def build_bundle_index(self, config: AiIosAgentConfig) -> JsonMap:
        return {"ok": True, "platformId": AiIosAgentConfig.PLATFORM_ID, "manifestHash": self.manifest_hash, "bundles": [{"id": bundle.id, "description": bundle.description, "toolCount": len([tool for tool in self.tools if matches_bundle(tool.id, bundle)])} for bundle in self._bundles]}

    def try_build_bundle(self, config: AiIosAgentConfig, bundle_id: str) -> JsonMap | None:
        bundle = next((item for item in self._bundles if item.id == bundle_id), None)
        if bundle is None:
            return None
        return {"ok": True, "platformId": AiIosAgentConfig.PLATFORM_ID, "manifestHash": self.manifest_hash, "bundle": {"id": bundle.id, "description": bundle.description}, "tools": [tool.to_summary_json() for tool in self.tools if matches_bundle(tool.id, bundle)]}

    def build_manifest_search(self, config: AiIosAgentConfig, *, query: str = "", limit: int = 0, namespace_id: str = "", bundle_id: str = "") -> JsonMap:
        safe_limit = clamp_number(limit or 8, 1, 64)
        candidates = self.tools
        if namespace_id:
            candidates = [tool for tool in candidates if tool.namespace_id == namespace_id]
        if bundle_id:
            bundle = next((item for item in self._bundles if item.id == bundle_id), None)
            if bundle is not None:
                candidates = [tool for tool in candidates if matches_bundle(tool.id, bundle)]
        tokens = [token for token in query.lower().split() if token]
        scored = [(tool, search_score(tool, tokens)) for tool in candidates]
        scored.sort(key=lambda item: (-item[1], item[0].id))
        tools = [tool.to_summary_json() for tool, score in scored if not tokens or score > 0][:safe_limit]
        return {"ok": True, "platformId": AiIosAgentConfig.PLATFORM_ID, "manifestHash": self.manifest_hash, "query": query, "namespaceId": namespace_id, "bundleId": bundle_id, "returned": len(tools), "tools": tools}

    def build_describe_many(self, config: AiIosAgentConfig, ids: list[str]) -> JsonMap:
        found: list[JsonMap] = []
        missing: list[str] = []
        for tool_id in ids:
            tool = self.find_tool(str(tool_id))
            if tool is None:
                missing.append(str(tool_id))
            else:
                found.append(tool.to_full_json())
        return {"ok": True, "platformId": AiIosAgentConfig.PLATFORM_ID, "manifestHash": self.manifest_hash, "returned": len(found), "missing": missing, "tools": found}


def clamp_number(value: int, minimum: int, maximum: int) -> int:
    return max(minimum, min(maximum, int(value)))


def derive_namespace_id(tool_id: str) -> str:
    return tool_id.split(".", 1)[0]


def schema_json(schema: JsonMap) -> str:
    import json
    return json.dumps(schema, indent=2, ensure_ascii=True)


def ensure_json_object(value: Any) -> JsonMap:
    return value if isinstance(value, dict) else {}


def matches_bundle(tool_id: str, bundle: AiManifestBundleDefinition) -> bool:
    return any(tool_id.startswith(prefix) for prefix in bundle.prefixes)


def search_score(tool: AiToolDefinition, tokens: list[str]) -> int:
    if not tokens:
        return 1
    haystack = f"{tool.id} {tool.namespace_id} {tool.description}".lower()
    description = tool.description.lower()
    tool_id = tool.id.lower()
    score = 0
    for token in tokens:
        if token in tool_id:
            score += 5
        elif token in description:
            score += 2
        elif token in haystack:
            score += 1
    return score


def iso_timestamp() -> str:
    from datetime import datetime, timezone
    return datetime.now(timezone.utc).isoformat()
