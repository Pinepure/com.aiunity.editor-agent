from __future__ import annotations

import json
from typing import Any
from urllib import error, parse, request

from .models import AiFigmaAgentConfig


def figma_request(
    config: AiFigmaAgentConfig,
    *,
    token: str,
    method: str,
    path: str,
    query: dict[str, Any] | None = None,
    body: dict[str, Any] | None = None,
) -> Any:
    if not token:
        raise RuntimeError("Figma API token is required for this tool.")
    url = build_url(config.figma_base_url, path, query=query)
    headers = {
        "Accept": "application/json",
        "User-Agent": f"ai-figma-agent/{config.service_version}",
        "X-Figma-Token": token,
    }
    raw_body = None
    if body is not None:
        raw_body = json.dumps(body, ensure_ascii=True).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = request.Request(url, data=raw_body, headers=headers, method=method.upper())
    try:
        with request.urlopen(req, timeout=config.tool_timeout_seconds) as response:
            payload = response.read().decode("utf-8")
    except error.HTTPError as exc:
        payload = exc.read().decode("utf-8", errors="replace")
        message = payload.strip()
        parsed = _try_json(payload)
        if isinstance(parsed, dict):
            message = str(parsed.get("message") or parsed.get("err") or parsed.get("error") or message)
        raise RuntimeError(f"Figma API {method.upper()} {path} failed with HTTP {exc.code}: {message}") from exc
    except error.URLError as exc:
        raise RuntimeError(f"Figma API {method.upper()} {path} failed: {exc.reason}") from exc
    parsed = _try_json(payload)
    return parsed if parsed is not None else payload


def build_url(base_url: str, path: str, *, query: dict[str, Any] | None = None) -> str:
    normalized_base = base_url.rstrip("/")
    normalized_path = path if path.startswith("/") else f"/{path}"
    if not query:
        return f"{normalized_base}{normalized_path}"
    encoded_query: list[tuple[str, str]] = []
    for key, value in query.items():
        if value is None or value == "":
            continue
        if isinstance(value, (list, tuple)):
            for item in value:
                if item is not None and item != "":
                    encoded_query.append((key, str(item)))
        else:
            encoded_query.append((key, str(value).lower() if isinstance(value, bool) else str(value)))
    return f"{normalized_base}{normalized_path}?{parse.urlencode(encoded_query)}" if encoded_query else f"{normalized_base}{normalized_path}"


def _try_json(payload: str) -> Any:
    payload = payload.strip()
    if not payload:
        return None
    try:
        return json.loads(payload)
    except json.JSONDecodeError:
        return None
