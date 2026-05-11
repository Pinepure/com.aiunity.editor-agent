const crypto = require('node:crypto');

const { clampNumber } = require('./models');

class ResultHandleStore {
  constructor(maxHandles = 96) {
    this.maxHandles = maxHandles;
    this.entries = new Map();
    this.order = [];
  }

  buildItemsResult({ sourceToolId, fieldName, items, summary, pageSize = 20 }) {
    const safePageSize = clampNumber(pageSize || 20, 1, 200);
    const returned = Math.min(safePageSize, items.length);
    const hasMore = returned < items.length;
    const response = {
      summary,
      returned,
      pageSize: safePageSize,
      total: items.length,
      hasMore,
      [fieldName]: items.slice(0, returned),
    };
    if (hasMore) {
      response.resultHandle = this.#createItemsHandle({ sourceToolId, fieldName, items, summary });
    }
    return response;
  }

  buildTextResult({ sourceToolId, text, summary, offset = 0, length = 4096 }) {
    const safeOffset = clampNumber(offset || 0, 0, text.length);
    const safeLength = clampNumber(length || 4096, 1, 32768);
    const count = Math.min(safeLength, text.length - safeOffset);
    const hasMore = safeOffset + count < text.length;
    const response = {
      summary,
      offset: safeOffset,
      limit: safeLength,
      count,
      totalChars: text.length,
      hasMore,
      content: count > 0 ? text.slice(safeOffset, safeOffset + count) : '',
    };
    if (hasMore) {
      response.resultHandle = this.#createTextHandle({ sourceToolId, summary, text });
    }
    return response;
  }

  buildPage(handleId, { offset = 0, limit = 0 } = {}) {
    const entry = this.entries.get(handleId);
    if (!entry) {
      return null;
    }
    return entry.kind === 'text'
      ? this.#buildTextPage(entry, { offset, limit })
      : this.#buildItemsPage(entry, { offset, limit });
  }

  #createItemsHandle({ sourceToolId, fieldName, items, summary }) {
    const entry = {
      id: this.#newHandleId(),
      kind: 'items',
      sourceToolId,
      fieldName,
      createdAt: new Date().toISOString(),
      summary,
      items,
    };
    this.#add(entry);
    return entry.id;
  }

  #createTextHandle({ sourceToolId, summary, text }) {
    const entry = {
      id: this.#newHandleId(),
      kind: 'text',
      sourceToolId,
      fieldName: 'content',
      createdAt: new Date().toISOString(),
      summary,
      text,
    };
    this.#add(entry);
    return entry.id;
  }

  #add(entry) {
    this.entries.set(entry.id, entry);
    this.order.push(entry.id);
    while (this.order.length > this.maxHandles) {
      const oldest = this.order.shift();
      this.entries.delete(oldest);
    }
  }

  #buildItemsPage(entry, { offset, limit }) {
    const safeOffset = clampNumber(offset || 0, 0, entry.items.length);
    const safeLimit = clampNumber(limit || 20, 1, 200);
    const count = Math.min(safeLimit, entry.items.length - safeOffset);
    return {
      ok: true,
      handleId: entry.id,
      kind: 'items',
      sourceToolId: entry.sourceToolId,
      fieldName: entry.fieldName,
      createdAt: entry.createdAt,
      offset: safeOffset,
      limit: safeLimit,
      count,
      total: entry.items.length,
      hasMore: safeOffset + count < entry.items.length,
      summary: entry.summary,
      [entry.fieldName]: entry.items.slice(safeOffset, safeOffset + count),
    };
  }

  #buildTextPage(entry, { offset, limit }) {
    const safeOffset = clampNumber(offset || 0, 0, entry.text.length);
    const safeLimit = clampNumber(limit || 4096, 1, 32768);
    const count = Math.min(safeLimit, entry.text.length - safeOffset);
    return {
      ok: true,
      handleId: entry.id,
      kind: 'text',
      sourceToolId: entry.sourceToolId,
      createdAt: entry.createdAt,
      offset: safeOffset,
      limit: safeLimit,
      count,
      totalChars: entry.text.length,
      hasMore: safeOffset + count < entry.text.length,
      summary: entry.summary,
      content: count > 0 ? entry.text.slice(safeOffset, safeOffset + count) : '',
    };
  }

  #newHandleId() {
    return `${Date.now().toString(16)}${crypto.randomBytes(4).toString('hex')}`;
  }
}

module.exports = {
  ResultHandleStore,
};
