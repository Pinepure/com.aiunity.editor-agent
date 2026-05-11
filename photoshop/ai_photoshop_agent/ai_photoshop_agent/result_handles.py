from __future__ import annotations

from dataclasses import dataclass
from uuid import uuid4

from .models import JsonMap, clamp_number, iso_timestamp


@dataclass
class _Entry:
    id: str
    kind: str
    source_tool_id: str
    field_name: str
    created_at: str
    summary: JsonMap
    items: list[object] | None = None
    text: str = ""


class ResultHandleStore:
    def __init__(self, max_handles: int = 96) -> None:
        self._entries: dict[str, _Entry] = {}
        self._order: list[str] = []
        self._max_handles = max_handles

    def build_items_result(self, source_tool_id: str, field_name: str, items: list[object], summary: JsonMap, page_size: int) -> JsonMap:
        safe_page_size = clamp_number(page_size, 1, 200)
        returned = min(len(items), safe_page_size)
        payload: JsonMap = {
            "summary": summary,
            "returned": returned,
            "pageSize": safe_page_size,
            "total": len(items),
            "hasMore": returned < len(items),
            field_name: items[:returned],
        }
        if returned < len(items):
            handle_id = self._add(_Entry(id=self._new_handle_id(), kind="items", source_tool_id=source_tool_id, field_name=field_name, created_at=iso_timestamp(), summary=summary, items=list(items)))
            payload["resultHandle"] = handle_id
        return payload

    def build_page(self, handle_id: str, *, offset: int, limit: int) -> JsonMap | None:
        entry = self._entries.get(handle_id)
        if entry is None or entry.items is None:
            return None
        safe_offset = clamp_number(offset, 0, len(entry.items))
        safe_limit = clamp_number(limit or 20, 1, 200)
        count = min(safe_limit, len(entry.items) - safe_offset)
        return {
            "ok": True,
            "handleId": handle_id,
            "kind": "items",
            "sourceToolId": entry.source_tool_id,
            "fieldName": entry.field_name,
            "createdAt": entry.created_at,
            "offset": safe_offset,
            "limit": safe_limit,
            "count": count,
            "total": len(entry.items),
            "hasMore": safe_offset + count < len(entry.items),
            "summary": entry.summary,
            entry.field_name: entry.items[safe_offset : safe_offset + count],
        }

    def _add(self, entry: _Entry) -> str:
        self._entries[entry.id] = entry
        self._order.append(entry.id)
        while len(self._order) > self._max_handles:
            oldest = self._order.pop(0)
            self._entries.pop(oldest, None)
        return entry.id

    @staticmethod
    def _new_handle_id() -> str:
        return f"{uuid4().hex[:12]}{uuid4().hex[:12]}"
