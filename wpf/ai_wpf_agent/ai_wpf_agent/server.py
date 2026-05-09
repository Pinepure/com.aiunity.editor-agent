from __future__ import annotations

import json
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from secrets import token_hex
from urllib.parse import parse_qs, urlparse

from .models import AiRuntimeState, AiToolExecutionContext, AiToolRegistry, AiWpfAgentConfig, clamp_number, ensure_json_object
from .result_handles import ResultHandleStore
from .tools import default_bundles, register_default_tools


class AiWpfAgentServer:
    def __init__(self, config: AiWpfAgentConfig) -> None:
        self.config = config
        self.runtime_state = AiRuntimeState()
        self.result_handle_store = ResultHandleStore()
        self.registry = AiToolRegistry(bundles=default_bundles())
        self._initialized = False
        self._token = ""
        self._httpd: ThreadingHTTPServer | None = None
        self._agent_manual = (Path(__file__).resolve().parent.parent / "AGENT.md").read_text(encoding="utf-8")
        self._tool_context = AiToolExecutionContext(config=self.config, registry=self.registry, result_handle_store=self.result_handle_store, runtime_state=self.runtime_state, agent_manual=self._agent_manual, read_token=lambda: self._token, regenerate_token=self._regenerate_token, build_health_payload=lambda include_ok=False: self._build_health_payload(include_ok=include_ok), build_agent_brief_payload=lambda include_ok=False: self._build_agent_brief_payload(include_ok=include_ok))

    @property
    def is_running(self) -> bool:
        return self._httpd is not None

    def serve_forever(self) -> None:
        if not self._initialized:
            register_default_tools(self.registry, self._tool_context)
            self._initialized = True
        if self.config.require_token:
            self._token = self._ensure_token()
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
        if method == "OPTIONS":
            handler.send_response(204)
            handler.send_header("Access-Control-Allow-Origin", "*")
            handler.send_header("Access-Control-Allow-Headers", "Content-Type, X-AI-Agent-Token, X-Wpf-Ai-Token")
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
                self._send_json(handler, 401, {"ok": False, "error": f"Unauthorized. Provide {AiWpfAgentConfig.PRIMARY_TOKEN_HEADER} or {AiWpfAgentConfig.LEGACY_TOKEN_HEADER}."})
                return
            if path_name in {"manifest", "manifest/summary"} and method == "GET":
                query = parse_qs(parsed.query)
                detail = (query.get("detail") or [""])[0].lower()
                payload = self.registry.build_manifest_full(self.config) if detail == "full" else self.registry.build_manifest_summary(self.config)
                self._send_json(handler, 200, payload)
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
                payload = self.registry.build_manifest_search(self.config, query=str(body.get("query") or ""), limit=int(body.get("limit") or 0), namespace_id=str(body.get("namespaceId") or ""), bundle_id=str(body.get("bundleId") or ""))
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
        provided = handler.headers.get(AiWpfAgentConfig.PRIMARY_TOKEN_HEADER) or handler.headers.get(AiWpfAgentConfig.LEGACY_TOKEN_HEADER)
        return bool(provided and provided == self._token)

    def _read_body_json(self, handler: BaseHTTPRequestHandler) -> dict[str, object]:
        length = int(handler.headers.get("Content-Length") or "0")
        if length <= 0:
            return {}
        raw = handler.rfile.read(length).decode("utf-8")
        return {} if not raw.strip() else ensure_json_object(json.loads(raw))

    def _build_health_payload(self, *, include_ok: bool = False) -> dict[str, object]:
        payload = {"framework": AiWpfAgentConfig.FRAMEWORK_NAME, "service": AiWpfAgentConfig.SERVICE_NAME, "serviceId": AiWpfAgentConfig.SERVICE_ID, "version": self.config.service_version, "platformId": AiWpfAgentConfig.PLATFORM_ID, "protocolVersion": AiWpfAgentConfig.PROTOCOL_VERSION, "serverRunning": self.is_running, "requiresToken": self.config.require_token, "acceptedTokenHeaders": self.config.accepted_token_headers, "serverUrl": self.config.server_url, "manifestHash": self.registry.manifest_hash, "toolCount": self.registry.count, "namespaces": self.registry.namespace_infos, "supportsManifestSearch": True, "supportsDescribeMany": True, "supportsResultHandles": True, "supportsBundles": True, "supportsTextChunking": True, "supportsDynamicToolRegistration": False, "recommendedFlow": ["GET /health and compare manifestHash before refreshing capabilities.", "Use POST /manifest/search or GET /manifest/bundle/{id} to narrow candidate tools.", "Use POST /tool/describe_many for exact argument and return schemas before calling tools.", "Use GET /result/{handleId} for additional pages or text chunks when a tool returns a resultHandle.", "Use GET /manifest/full only as a fallback when search is insufficient."], "paths": {"health": "/health", "manifestSummary": "/manifest", "manifestFull": "/manifest/full", "manifestSearch": "/manifest/search", "manifestBundles": "/manifest/bundles", "toolDescribeMany": "/tool/describe_many", "call": "/call/{toolId}", "agent": "/agent", "agentBrief": "/agent/brief", "resultPage": "/result/{handleId}"}, "platform": {"dotnetExecutable": self.config.dotnet_executable, "msbuildExecutable": self.config.msbuild_executable, "solutionPath": self.config.solution_path or None, "projectDir": self.config.project_dir or None}}
        if include_ok:
            payload["ok"] = True
        return payload

    def _build_agent_brief_payload(self, *, include_ok: bool = False) -> dict[str, object]:
        payload = {"framework": AiWpfAgentConfig.FRAMEWORK_NAME, "platformId": AiWpfAgentConfig.PLATFORM_ID, "summary": "Use solution, project, XAML, and build tools through the shared discovery-first protocol instead of editing WPF work blindly.", "steps": ["Call GET /health and reuse cached capabilities while manifestHash stays unchanged.", "Search tools with POST /manifest/search or load a focused bundle with GET /manifest/bundle/{id}.", "Inspect solution and XAML structure before mutation or builds.", "Request exact tool schemas with POST /tool/describe_many before calling unfamiliar tools.", "When a tool returns resultHandle, page additional data through GET /result/{handleId} instead of re-running the tool with larger limits."], "paths": {"health": "/health", "manifestSummary": "/manifest", "manifestFull": "/manifest/full", "manifestSearch": "/manifest/search", "manifestBundles": "/manifest/bundles", "toolDescribeMany": "/tool/describe_many", "call": "/call/{toolId}", "agent": "/agent", "agentBrief": "/agent/brief", "resultPage": "/result/{handleId}"}}
        if include_ok:
            payload["ok"] = True
        return payload

    def _ensure_token(self) -> str:
        self.config.token_file_path.parent.mkdir(parents=True, exist_ok=True)
        if self.config.token_file_path.exists():
            token = self.config.token_file_path.read_text(encoding="utf-8").strip()
            if token:
                return token
        return self._regenerate_token()

    def _regenerate_token(self) -> str:
        token = token_hex(24)
        self.config.token_file_path.parent.mkdir(parents=True, exist_ok=True)
        self.config.token_file_path.write_text(token, encoding="utf-8")
        self._token = token
        self.runtime_state.log("info", "Token regenerated.")
        return token

    def _send_json(self, handler: BaseHTTPRequestHandler, status_code: int, payload: dict[str, object]) -> None:
        body = json.dumps(payload, indent=2, ensure_ascii=True).encode("utf-8")
        handler.send_response(status_code)
        handler.send_header("Access-Control-Allow-Origin", "*")
        handler.send_header("Access-Control-Allow-Headers", "Content-Type, X-AI-Agent-Token, X-Wpf-Ai-Token")
        handler.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        handler.send_header("Content-Type", "application/json; charset=utf-8")
        handler.send_header("Content-Length", str(len(body)))
        handler.end_headers()
        handler.wfile.write(body)
