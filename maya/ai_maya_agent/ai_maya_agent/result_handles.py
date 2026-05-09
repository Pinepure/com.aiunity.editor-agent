from __future__ import annotations

from dataclasses import dataclass, field
from secrets import token_hex
from typing import Any

from .models import clamp_number, iso_timestamp


@dataclass
class _ResultHandleEntry:
    id: str
    kind: str
    source_tool_id: str
    field_name: str
    created_at: str
    summary: dict[str, Any]
    items: list[Any] = field(default_factory=list)
    text: str = ""


class ResultHandleStore:
    def __init__(self, max_handles: int = 96) -> None:
        self.max_handles = max_handles
        self._entries: dict[str, _ResultHandleEntry] = {}
        self._order: list[str] = []

    def build_items_result(
        self,
        *,
        source_tool_id: str,
        field_name: str,
        items: list[Any],
        summary: dict[str, Any],
        page_size: int = 20,
    ) -> dict[str, Any]:
        safe_page_size = clamp_number(page_size or 20, 1, 200)
        returned = min(safe_page_size, len(items))
        has_more = returned < len(items)
        response = {
            "summary": summary,
            "returned": returned,
            "pageSize": safe_page_size,
            "total": len(items),
            "hasMore": has_more,
            field_name: items[:returned],
        }
        if has_more:
            response["resultHandle"] = self._create_items_handle(
                source_tool_id=source_tool_id,
                field_name=field_name,
                items=items,
                summary=summary,
            )
        return response

    def build_text_result(
        self,
        *,
        source_tool_id: str,
        text: str,
        summary: dict[str, Any],
        offset: int = 0,
        length: int = 4096,
    ) -> dict[str, Any]:
        safe_offset = clamp_number(offset or 0, 0, len(text))
        safe_length = clamp_number(length or 4096, 1, 32768)
        count = min(safe_length, len(text) - safe_offset)
        has_more = safe_offset + count < len(text)
        response = {
            "summary": summary,
            "offset": safe_offset,
            "limit": safe_length,
            "count": count,
            "totalChars": len(text),
            "hasMore": has_more,
            "content": text[safe_offset : safe_offset + count] if count > 0 else "",
        }
        if has_more:
            response["resultHandle"] = self._create_text_handle(
                source_tool_id=source_tool_id,
                summary=summary,
                text=text,
            )
        return response

    def build_page(self, handle_id: str, *, offset: int = 0, limit: int = 0) -> dict[str, Any] | None:
        entry = self._entries.get(handle_id)
        if entry is None:
            return None
        if entry.kind == "text":
            return self._build_text_page(entry, offset=offset, limit=limit)
        return self._build_items_page(entry, offset=offset, limit=limit)

    def _create_items_handle(
        self,
        *,
        source_tool_id: str,
        field_name: str,
        items: list[Any],
        summary: dict[str, Any],
    ) -> str:
        entry = _ResultHandleEntry(
            id=self._new_handle_id(),
            kind="items",
            source_tool_id=source_tool_id,
            field_name=field_name,
            created_at=iso_timestamp(),
            summary=summary,
            items=items,
        )
        self._add(entry)
        return entry.id

    def _create_text_handle(
        self,
        *,
        source_tool_id: str,
        summary: dict[str, Any],
        text: str,
    ) -> str:
        entry = _ResultHandleEntry(
            id=self._new_handle_id(),
            kind="text",
            source_tool_id=source_tool_id,
            field_name="content",
            created_at=iso_timestamp(),
            summary=summary,
            text=text,
        )
        self._add(entry)
        return entry.id

    def _add(self, entry: _ResultHandleEntry) -> None:
        self._entries[entry.id] = entry
        self._order.append(entry.id)
        while len(self._order) > self.max_handles:
            oldest = self._order.pop(0)
            self._entries.pop(oldest, None)

    def _build_items_page(self, entry: _ResultHandleEntry, *, offset: int, limit: int) -> dict[str, Any]:
        safe_offset = clamp_number(offset or 0, 0, len(entry.items))
        safe_limit = clamp_number(limit or 20, 1, 200)
        count = min(safe_limit, len(entry.items) - safe_offset)
        has_more = safe_offset + count < len(entry.items)
        return {
            "ok": True,
            "handleId": entry.id,
            "kind": "items",
            "sourceToolId": entry.source_tool_id,
            "fieldName": entry.field_name,
            "createdAt": entry.created_at,
            "offset": safe_offset,
            "limit": safe_limit,
            "count": count,
            "total": len(entry.items),
            "hasMore": has_more,
            "summary": entry.summary,
            entry.field_name: entry.items[safe_offset : safe_offset + count],
        }

    def _build_text_page(self, entry: _ResultHandleEntry, *, offset: int, limit: int) -> dict[str, Any]:
        safe_offset = clamp_number(offset or 0, 0, len(entry.text))
        safe_limit = clamp_number(limit or 4096, 1, 32768)
        count = min(safe_limit, len(entry.text) - safe_offset)
        has_more = safe_offset + count < len(entry.text)
        return {
            "ok": True,
            "handleId": entry.id,
            "kind": "text",
            "sourceToolId": entry.source_tool_id,
            "createdAt": entry.created_at,
            "offset": safe_offset,
            "limit": safe_limit,
            "count": count,
            "totalChars": len(entry.text),
            "hasMore": has_more,
            "summary": entry.summary,
            "content": entry.text[safe_offset : safe_offset + count] if count > 0 else "",
        }

    def _new_handle_id(self) -> str:
        return f"{int(__import__('time').time() * 1000000):x}{token_hex(4)}"
