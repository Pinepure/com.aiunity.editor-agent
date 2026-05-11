from __future__ import annotations

import json
import time
from pathlib import Path
from uuid import uuid4


class PhotoshopBridgeClient:
    def __init__(self, bridge_dir: Path, *, poll_interval_seconds: float = 0.5) -> None:
        self.bridge_dir = Path(bridge_dir).expanduser().resolve()
        self.poll_interval_seconds = max(0.1, poll_interval_seconds)

    @property
    def requests_dir(self) -> Path:
        return self.bridge_dir / "requests"

    @property
    def responses_dir(self) -> Path:
        return self.bridge_dir / "responses"

    @property
    def status_file_path(self) -> Path:
        return self.bridge_dir / "status.json"

    @property
    def generated_tools_dir(self) -> Path:
        return self.bridge_dir / "generated_tools"

    def ensure_layout(self) -> None:
        self.bridge_dir.mkdir(parents=True, exist_ok=True)
        self.requests_dir.mkdir(parents=True, exist_ok=True)
        self.responses_dir.mkdir(parents=True, exist_ok=True)
        self.generated_tools_dir.mkdir(parents=True, exist_ok=True)

    def read_status(self) -> dict[str, object]:
        self.ensure_layout()
        if not self.status_file_path.exists():
            return {"bridgeDir": str(self.bridge_dir), "pluginReady": False}
        try:
            payload = json.loads(self.status_file_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as error:
            return {"bridgeDir": str(self.bridge_dir), "pluginReady": False, "error": str(error)}
        payload["bridgeDir"] = str(self.bridge_dir)
        payload["pluginReady"] = bool(payload.get("pluginReady"))
        return payload

    def call(self, tool_id: str, args: dict[str, object], *, timeout_seconds: int) -> object:
        self.ensure_layout()
        request_id = uuid4().hex
        request_path = self.requests_dir / f"{request_id}.json"
        response_path = self.responses_dir / f"{request_id}.json"
        request_payload = {"requestId": request_id, "toolId": tool_id, "args": args, "createdAt": time.time()}
        request_path.write_text(json.dumps(request_payload, ensure_ascii=True, indent=2), encoding="utf-8")
        deadline = time.monotonic() + max(1, timeout_seconds)
        while time.monotonic() < deadline:
            if response_path.exists():
                raw = json.loads(response_path.read_text(encoding="utf-8"))
                response_path.unlink(missing_ok=True)
                request_path.unlink(missing_ok=True)
                if not raw.get("ok", False):
                    raise RuntimeError(str(raw.get("error") or f"Photoshop bridge call failed: {tool_id}"))
                return raw.get("result")
            time.sleep(self.poll_interval_seconds)
        raise TimeoutError(f"Timed out waiting for Photoshop bridge response for {tool_id}")
