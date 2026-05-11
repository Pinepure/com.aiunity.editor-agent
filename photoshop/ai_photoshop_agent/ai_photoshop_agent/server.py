from __future__ import annotations

import json
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from secrets import token_hex
from urllib.parse import parse_qs, urlparse

from .bridge import PhotoshopBridgeClient
from .generated_tools import PhotoshopGeneratedToolHost
from .models import AiPhotoshopAgentConfig, AiRuntimeState, AiToolExecutionContext, AiToolRegistry, clamp_number, ensure_json_object
from .result_handles import ResultHandleStore
from .tools import default_bundles, register_default_tools


class AiPhotoshopAgentServer:
    def __init__(self, config: AiPhotoshopAgentConfig) -> None:
        self.config = config
        self.runtime_state = AiRuntimeState()
        self.result_handle_store = ResultHandleStore()
        self.registry = AiToolRegistry(bundles=default_bundles())
        self.bridge_client = PhotoshopBridgeClient(config.bridge_dir, poll_interval_seconds=config.poll_interval_seconds)
        self.generated_tool_host = PhotoshopGeneratedToolHost(config=self.config, bridge_client=self.bridge_client)
        self._generated_tools_fingerprint = ""
        self._token = ""
        self._httpd: ThreadingHTTPServer | None = None
        self._agent_manual = (Path(__file__).resolve().parent.parent / "AGENT.md").read_text(encoding="utf-8")
        self._tool_context = AiToolExecutionContext(
            config=self.config,
            registry=self.registry,
            result_handle_store=self.result_handle_store,
            runtime_state=self.runtime_state,
            agent_manual=self._agent_manual,
            read_token=lambda: self._token,
            regenerate_token=self._regenerate_token,
            build_health_payload=lambda include_ok=False: self._build_health_payload(include_ok=include_ok),
            build_agent_brief_payload=lambda include_ok=False: self._build_agent_brief_payload(include_ok=include_ok),
            bridge_client=self.bridge_client,
            generated_tool_host=self.generated_tool_host,
            reload_generated_tools=lambda force=False: self._refresh_registry(force=force),
        )

    @property
    def is_running(self) -> bool:
        return self._httpd is not None

    def serve_forever(self) -> None:
        self._refresh_registry(force=True)
        if self.config.require_token:
            self._token = self._ensure_token()
        self.bridge_client.ensure_layout()

        outer = self

        class Handler(BaseHTTPRequestHandler):
            def do_OPTIONS(self) -> None:  # noqa: N802
                outer._handle_request(self, method="OPTIONS")

            def do_GET(self) -> None:  # noqa: N802
                outer._handle_request(self, method="GET")

            def do_POST(self) -> None:  # noqa: N802
                outer._handle_request(self, method="POST")

            def log_message(self, format: str, *args) -> None:  # noqa: A003
                return

        self._httpd = ThreadingHTTPServer((self.config.host, self.config.port), Handler)
        self.runtime_state.log("info", f"Service started at {self.config.server_url}")
        try:
            self._httpd.serve_forever()
        finally:
            self._httpd.server_close()
            self._httpd = None

    def stop(self) -> None:
        if self._httpd is not None:
            self._httpd.shutdown()

    def _handle_request(self, handler: BaseHTTPRequestHandler, *, method: str) -> None:
        self._refresh_registry()
        if method == "OPTIONS":
            handler.send_response(204)
            handler.send_header("Access-Control-Allow-Origin", "*")
            handler.send_header("Access-Control-Allow-Headers", "Content-Type, X-AI-Agent-Token, X-Photoshop-Ai-Token")
            handler.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
            handler.send_header("Content-Length", "0")
            handler.end_headers()
            return
        try:
            parsed = urlparse(handler.path)
            path_name = parsed.path.lstrip("/")
            if path_name == "health" and method == "GET":
                self._send_json(handler, 200, self._build_health_payload(include_ok=True))
                return
            if not self._is_authorized(handler):
                self._send_json(handler, 401, {"ok": False, "error": f"Unauthorized. Provide {AiPhotoshopAgentConfig.PRIMARY_TOKEN_HEADER} or {AiPhotoshopAgentConfig.LEGACY_TOKEN_HEADER}."})
                return
            if path_name in {"manifest", "manifest/summary"} and method == "GET":
                self._send_json(handler, 200, self.registry.build_manifest_summary(self.config))
                return
            if path_name == "manifest/full" and method == "GET":
                self._send_json(handler, 200, self.registry.build_manifest_full(self.config))
                return
            if path_name == "manifest/bundles" and method == "GET":
                self._send_json(handler, 200, self.registry.build_bundle_index(self.config))
                return
            if path_name.startswith("manifest/bundle/") and method == "GET":
                bundle_id = path_name[len("manifest/bundle/") :]
                payload = self.registry.try_build_bundle(self.config, bundle_id)
                if payload is None:
                    self._send_json(handler, 404, {"ok": False, "error": f"Unknown manifest bundle: {bundle_id}"})
                    return
                self._send_json(handler, 200, payload)
                return
            if path_name == "manifest/search" and method == "POST":
                body = self._read_body_json(handler)
                payload = self.registry.build_manifest_search(
                    self.config,
                    query=str(body.get("query") or ""),
                    limit=int(body.get("limit") or 0),
                    namespace_id=str(body.get("namespaceId") or ""),
                    bundle_id=str(body.get("bundleId") or ""),
                )
                self._send_json(handler, 200, payload)
                return
            if path_name == "tool/describe_many" and method == "POST":
                body = self._read_body_json(handler)
                ids = [str(item) for item in body.get("ids", [])] if isinstance(body.get("ids", []), list) else []
                self._send_json(handler, 200, self.registry.build_describe_many(self.config, ids))
                return
            if path_name == "agent/brief" and method == "GET":
                self._send_json(handler, 200, self._build_agent_brief_payload(include_ok=True))
                return
            if path_name == "agent" and method == "GET":
                self._send_json(handler, 200, {"ok": True, "content": self._agent_manual})
                return
            if path_name.startswith("result/") and method == "GET":
                handle_id = path_name[len("result/") :]
                query = parse_qs(parsed.query)
                payload = self.result_handle_store.build_page(handle_id, offset=clamp_number(int((query.get("offset") or ["0"])[0]), 0, 10**9), limit=clamp_number(int((query.get("limit") or ["0"])[0]), 0, 32768))
                if payload is None:
                    self._send_json(handler, 404, {"ok": False, "error": f"Unknown result handle: {handle_id}"})
                    return
                self._send_json(handler, 200, payload)
                return
            if path_name.startswith("call/") and method == "POST":
                tool_id = path_name[len("call/") :]
                tool = self.registry.find_tool(tool_id)
                if tool is None:
                    self._send_json(handler, 404, {"ok": False, "toolId": tool_id, "error": f"Unknown tool: {tool_id}"})
                    return
                if tool.requires_confirmation and not self.config.full_access_enabled:
                    self._send_json(handler, 403, {"ok": False, "toolId": tool_id, "error": "High-risk tool requires --full-access for this adapter."})
                    return
                body = self._read_body_json(handler)
                started_at = time.monotonic()
                try:
                    result = tool.handler(body, self._tool_context)
                    duration_ms = int((time.monotonic() - started_at) * 1000)
                    self.runtime_state.record_call(tool_id=tool_id, ok=True, duration_ms=duration_ms, message="ok")
                    self._send_json(handler, 200, {"ok": True, "toolId": tool_id, "durationMs": duration_ms, "result": result})
                except Exception as error:  # noqa: BLE001
                    duration_ms = int((time.monotonic() - started_at) * 1000)
                    self.runtime_state.record_call(tool_id=tool_id, ok=False, duration_ms=duration_ms, message=str(error))
                    self._send_json(handler, 500, {"ok": False, "toolId": tool_id, "durationMs": duration_ms, "error": str(error)})
                return
            self._send_json(handler, 404, {"ok": False, "error": f"Not found: {path_name}"})
        except Exception as error:  # noqa: BLE001
            self.runtime_state.log("error", str(error))
            self._send_json(handler, 500, {"ok": False, "error": str(error)})

    def _is_authorized(self, handler: BaseHTTPRequestHandler) -> bool:
        if not self.config.require_token:
            return True
        provided = handler.headers.get(AiPhotoshopAgentConfig.PRIMARY_TOKEN_HEADER) or handler.headers.get(AiPhotoshopAgentConfig.LEGACY_TOKEN_HEADER)
        return bool(provided and provided == self._token)

    def _read_body_json(self, handler: BaseHTTPRequestHandler) -> dict[str, object]:
        length = int(handler.headers.get("Content-Length") or "0")
        if length <= 0:
            return {}
        raw = handler.rfile.read(length).decode("utf-8")
        return {} if not raw.strip() else ensure_json_object(json.loads(raw))

    def _build_health_payload(self, *, include_ok: bool = False) -> dict[str, object]:
        return {
            **({"ok": True} if include_ok else {}),
            "framework": AiPhotoshopAgentConfig.FRAMEWORK_NAME,
            "service": AiPhotoshopAgentConfig.SERVICE_NAME,
            "serviceId": AiPhotoshopAgentConfig.SERVICE_ID,
            "version": self.config.service_version,
            "platformId": AiPhotoshopAgentConfig.PLATFORM_ID,
            "protocolVersion": AiPhotoshopAgentConfig.PROTOCOL_VERSION,
            "serverRunning": self.is_running,
            "requiresToken": self.config.require_token,
            "acceptedTokenHeaders": self.config.accepted_token_headers,
            "serverUrl": self.config.server_url,
            "manifestHash": self.registry.manifest_hash,
            "toolCount": self.registry.count,
            "namespaces": self.registry.namespace_infos,
            "supportsManifestSearch": True,
            "supportsDescribeMany": True,
            "supportsResultHandles": True,
            "supportsBundles": True,
            "supportsTextChunking": False,
            "supportsDynamicToolRegistration": True,
            "recommendedFlow": [
                "GET /health and compare manifestHash before refreshing capabilities.",
                "Use POST /manifest/search or GET /manifest/bundle/{id} to narrow candidate tools.",
                "Use POST /tool/describe_many for exact argument and return schemas before calling tools.",
                "Use GET /result/{handleId} for additional pages when a tool returns a resultHandle.",
                "Use GET /manifest/full only as a fallback when search is insufficient.",
            ],
            "paths": {"health": "/health", "manifestSummary": "/manifest", "manifestFull": "/manifest/full", "manifestSearch": "/manifest/search", "manifestBundles": "/manifest/bundles", "toolDescribeMany": "/tool/describe_many", "call": "/call/{toolId}", "agent": "/agent", "agentBrief": "/agent/brief", "resultPage": "/result/{handleId}"},
            "platform": {
                "bridgeDir": str(self.config.bridge_dir),
                "generatedToolsDirectoryPath": str(self.config.generated_tools_directory_path),
                "bridgeStatus": self.bridge_client.read_status(),
            },
        }

    def _build_agent_brief_payload(self, *, include_ok: bool = False) -> dict[str, object]:
        return {
            **({"ok": True} if include_ok else {}),
            "framework": AiPhotoshopAgentConfig.FRAMEWORK_NAME,
            "platformId": AiPhotoshopAgentConfig.PLATFORM_ID,
            "summary": "Use the Photoshop bridge-backed UXP plugin for document and layer inspection or mutation while keeping host execution on official Photoshop DOM APIs.",
            "steps": [
                "Call GET /health and confirm the bridge heartbeat before assuming Photoshop is reachable.",
                "Search tools with POST /manifest/search or load a focused bundle with GET /manifest/bundle/{id}.",
                "Inspect documents and layers before calling any mutation tool.",
                "Request exact tool schemas with POST /tool/describe_many before calling unfamiliar tools.",
            ],
            "paths": {"health": "/health", "manifestSummary": "/manifest", "manifestFull": "/manifest/full", "manifestSearch": "/manifest/search", "manifestBundles": "/manifest/bundles", "toolDescribeMany": "/tool/describe_many", "call": "/call/{toolId}", "agent": "/agent", "agentBrief": "/agent/brief", "resultPage": "/result/{handleId}"},
        }

    def _ensure_token(self) -> str:
        self.config.state_directory_path.mkdir(parents=True, exist_ok=True)
        if self.config.token_file_path.exists():
            existing = self.config.token_file_path.read_text(encoding="utf-8").strip()
            if existing:
                return existing
        return self._regenerate_token()

    def _regenerate_token(self) -> str:
        self.config.state_directory_path.mkdir(parents=True, exist_ok=True)
        token = token_hex(24)
        self.config.token_file_path.write_text(token, encoding="utf-8")
        self._token = token
        self.runtime_state.log("info", "Token regenerated.")
        return token

    @staticmethod
    def _send_json(handler: BaseHTTPRequestHandler, status_code: int, payload: dict[str, object]) -> None:
        body = json.dumps(payload, ensure_ascii=True, indent=2).encode("utf-8")
        handler.send_response(status_code)
        handler.send_header("Access-Control-Allow-Origin", "*")
        handler.send_header("Access-Control-Allow-Headers", "Content-Type, X-AI-Agent-Token, X-Photoshop-Ai-Token")
        handler.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        handler.send_header("Content-Type", "application/json; charset=utf-8")
        handler.send_header("Content-Length", str(len(body)))
        handler.end_headers()
        handler.wfile.write(body)

    def _refresh_registry(self, *, force: bool = False) -> dict[str, object]:
        fingerprint = self.generated_tool_host.compute_fingerprint()
        if not force and fingerprint == self._generated_tools_fingerprint and self.registry.count > 0:
            return {
                "reloaded": False,
                "manifestHash": self.registry.manifest_hash,
                "generatedToolCount": len(self.generated_tool_host.list_definitions()),
            }

        self.registry.reset()
        register_default_tools(self.registry, self._tool_context)
        generated_definitions = self.generated_tool_host.load_definitions()
        for definition in generated_definitions:
            self.registry.register(self.generated_tool_host.create_tool_definition(definition, self._tool_context))
        self._generated_tools_fingerprint = fingerprint
        self.runtime_state.log("info", f"Generated tool registry refreshed. Loaded {len(generated_definitions)} generated tool(s).")
        return {
            "reloaded": True,
            "manifestHash": self.registry.manifest_hash,
            "generatedToolCount": len(generated_definitions),
        }
