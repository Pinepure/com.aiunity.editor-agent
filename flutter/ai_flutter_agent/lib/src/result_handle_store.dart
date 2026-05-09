import 'dart:math';

class ResultHandleStore {
  ResultHandleStore({this.maxHandles = 96});

  final int maxHandles;
  final Map<String, _ResultHandleEntry> _entries = <String, _ResultHandleEntry>{};
  final List<String> _order = <String>[];

  Map<String, dynamic> buildItemsResult({
    required String sourceToolId,
    required String fieldName,
    required List<Object?> items,
    required Map<String, dynamic> summary,
    int pageSize = 20,
  }) {
    final safePageSize = pageSize <= 0 ? 20 : pageSize.clamp(1, 200).toInt();
    final returned = min(safePageSize, items.length);
    final hasMore = returned < items.length;
    final response = <String, dynamic>{
      'summary': summary,
      'returned': returned,
      'pageSize': safePageSize,
      'total': items.length,
      'hasMore': hasMore,
      fieldName: items.take(returned).toList(),
    };

    if (hasMore) {
      final handleId = _createItemsHandle(
        sourceToolId: sourceToolId,
        fieldName: fieldName,
        items: items,
        summary: summary,
      );
      response['resultHandle'] = handleId;
    }

    return response;
  }

  Map<String, dynamic> buildTextResult({
    required String sourceToolId,
    required String text,
    required Map<String, dynamic> summary,
    int offset = 0,
    int length = 4096,
  }) {
    final safeOffset = offset.clamp(0, text.length).toInt();
    final safeLength = length <= 0 ? 4096 : length.clamp(1, 32768).toInt();
    final count = min(safeLength, text.length - safeOffset);
    final hasMore = safeOffset + count < text.length;
    final chunk = count <= 0 ? '' : text.substring(safeOffset, safeOffset + count);

    final response = <String, dynamic>{
      'summary': summary,
      'offset': safeOffset,
      'limit': safeLength,
      'count': count,
      'totalChars': text.length,
      'hasMore': hasMore,
      'content': chunk,
    };

    if (hasMore) {
      final handleId = _createTextHandle(
        sourceToolId: sourceToolId,
        summary: summary,
        text: text,
      );
      response['resultHandle'] = handleId;
    }

    return response;
  }

  Map<String, dynamic>? buildPage(String handleId, {int offset = 0, int limit = 0}) {
    final entry = _entries[handleId];
    if (entry == null) {
      return null;
    }

    if (entry.kind == _ResultHandleKind.text) {
      return _buildTextPage(entry, offset: offset, limit: limit);
    }
    return _buildItemsPage(entry, offset: offset, limit: limit);
  }

  String _createItemsHandle({
    required String sourceToolId,
    required String fieldName,
    required List<Object?> items,
    required Map<String, dynamic> summary,
  }) {
    final entry = _ResultHandleEntry(
      id: _newHandleId(),
      kind: _ResultHandleKind.items,
      sourceToolId: sourceToolId,
      fieldName: fieldName,
      createdAt: DateTime.now().toUtc().toIso8601String(),
      summary: summary,
      items: List<Object?>.from(items),
    );
    _add(entry);
    return entry.id;
  }

  String _createTextHandle({
    required String sourceToolId,
    required Map<String, dynamic> summary,
    required String text,
  }) {
    final entry = _ResultHandleEntry(
      id: _newHandleId(),
      kind: _ResultHandleKind.text,
      sourceToolId: sourceToolId,
      fieldName: 'content',
      createdAt: DateTime.now().toUtc().toIso8601String(),
      summary: summary,
      text: text,
    );
    _add(entry);
    return entry.id;
  }

  void _add(_ResultHandleEntry entry) {
    _entries[entry.id] = entry;
    _order.add(entry.id);
    while (_order.length > maxHandles) {
      final oldest = _order.removeAt(0);
      _entries.remove(oldest);
    }
  }

  Map<String, dynamic> _buildItemsPage(
    _ResultHandleEntry entry, {
    required int offset,
    required int limit,
  }) {
    final items = entry.items;
    final safeOffset = offset.clamp(0, items.length).toInt();
    final safeLimit = limit <= 0 ? 20 : limit.clamp(1, 200).toInt();
    final count = min(safeLimit, items.length - safeOffset);
    final hasMore = safeOffset + count < items.length;
    return <String, dynamic>{
      'ok': true,
      'handleId': entry.id,
      'kind': 'items',
      'sourceToolId': entry.sourceToolId,
      'fieldName': entry.fieldName,
      'createdAt': entry.createdAt,
      'offset': safeOffset,
      'limit': safeLimit,
      'count': count,
      'total': items.length,
      'hasMore': hasMore,
      'summary': entry.summary,
      entry.fieldName: items.skip(safeOffset).take(count).toList(),
    };
  }

  Map<String, dynamic> _buildTextPage(
    _ResultHandleEntry entry, {
    required int offset,
    required int limit,
  }) {
    final text = entry.text;
    final safeOffset = offset.clamp(0, text.length).toInt();
    final safeLimit = limit <= 0 ? 4096 : limit.clamp(1, 32768).toInt();
    final count = min(safeLimit, text.length - safeOffset);
    final hasMore = safeOffset + count < text.length;
    return <String, dynamic>{
      'ok': true,
      'handleId': entry.id,
      'kind': 'text',
      'sourceToolId': entry.sourceToolId,
      'createdAt': entry.createdAt,
      'offset': safeOffset,
      'limit': safeLimit,
      'count': count,
      'totalChars': text.length,
      'hasMore': hasMore,
      'summary': entry.summary,
      'content': count <= 0 ? '' : text.substring(safeOffset, safeOffset + count),
    };
  }

  String _newHandleId() {
    final random = Random.secure();
    final timestamp = DateTime.now().microsecondsSinceEpoch.toRadixString(16);
    final suffix = random.nextInt(0x7fffffff).toRadixString(16).padLeft(8, '0');
    return '$timestamp$suffix';
  }
}

enum _ResultHandleKind { items, text }

class _ResultHandleEntry {
  _ResultHandleEntry({
    required this.id,
    required this.kind,
    required this.sourceToolId,
    required this.fieldName,
    required this.createdAt,
    required this.summary,
    this.items = const <Object?>[],
    this.text = '',
  });

  final String id;
  final _ResultHandleKind kind;
  final String sourceToolId;
  final String fieldName;
  final String createdAt;
  final Map<String, dynamic> summary;
  final List<Object?> items;
  final String text;
}
