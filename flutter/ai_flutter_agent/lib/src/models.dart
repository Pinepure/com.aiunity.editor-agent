import 'dart:convert';

import 'result_handle_store.dart';

typedef JsonMap = Map<String, dynamic>;
typedef AiToolHandler = Future<Object?> Function(JsonMap args, AiToolExecutionContext context);

enum AiToolDanger {
  low,
  medium,
  high,
}

extension AiToolDangerWireValue on AiToolDanger {
  String get wireValue {
    switch (this) {
      case AiToolDanger.low:
        return 'low';
      case AiToolDanger.medium:
        return 'medium';
      case AiToolDanger.high:
        return 'high';
    }
  }
}

class AiFlutterAgentConfig {
  const AiFlutterAgentConfig({
    required this.projectRoot,
    this.host = '127.0.0.1',
    this.port = 19777,
    this.requireToken = true,
    this.fullAccessEnabled = false,
    this.flutterExecutable = 'flutter',
    this.toolTimeoutMs = 120000,
    this.serviceVersion = '0.1.0',
  });

  static const String frameworkName = 'AI Platform Agent Framework';
  static const String protocolVersion = '2.0';
  static const String serviceId = 'aiflutter.agent';
  static const String serviceName = 'AI Flutter Agent';
  static const String platformId = 'flutter';
  static const String primaryTokenHeader = 'X-AI-Agent-Token';
  static const String legacyTokenHeader = 'X-Flutter-Ai-Token';

  final String projectRoot;
  final String host;
  final int port;
  final bool requireToken;
  final bool fullAccessEnabled;
  final String flutterExecutable;
  final int toolTimeoutMs;
  final String serviceVersion;

  List<String> get acceptedTokenHeaders => const <String>[
        primaryTokenHeader,
        legacyTokenHeader,
      ];

  String get serverUrl => 'http://$host:$port';

  String get stateDirectoryPath => _join(projectRoot, '.ai_platform_agent');

  String get tokenFilePath => _join(stateDirectoryPath, 'token.txt');
}

class AiRuntimeState {
  final List<JsonMap> _serviceLogs = <JsonMap>[];
  final List<JsonMap> _toolCalls = <JsonMap>[];

  void log(String level, String message) {
    _serviceLogs.add(<String, dynamic>{
      'time': DateTime.now().toUtc().toIso8601String(),
      'level': level,
      'message': message,
    });
    if (_serviceLogs.length > 400) {
      _serviceLogs.removeAt(0);
    }
  }

  void recordCall({
    required String toolId,
    required bool ok,
    required int durationMs,
    required String message,
  }) {
    _toolCalls.add(<String, dynamic>{
      'time': DateTime.now().toUtc().toIso8601String(),
      'toolId': toolId,
      'ok': ok,
      'durationMs': durationMs,
      'message': message,
    });
    if (_toolCalls.length > 400) {
      _toolCalls.removeAt(0);
    }
  }

  List<JsonMap> recentLogs(int maxEntries) {
    final safeMax = maxEntries <= 0 ? 100 : maxEntries.clamp(1, 300).toInt();
    return List<JsonMap>.from(_serviceLogs.reversed.take(safeMax).toList().reversed);
  }

  List<JsonMap> recentCalls(int maxEntries) {
    final safeMax = maxEntries <= 0 ? 100 : maxEntries.clamp(1, 300).toInt();
    return List<JsonMap>.from(_toolCalls.reversed.take(safeMax).toList().reversed);
  }
}

class AiManifestBundleDefinition {
  const AiManifestBundleDefinition({
    required this.id,
    required this.description,
    required this.prefixes,
  });

  final String id;
  final String description;
  final List<String> prefixes;
}

class AiToolDefinition {
  AiToolDefinition({
    required this.id,
    required this.description,
    required this.argsSchemaJson,
    required this.returnSchemaJson,
    required this.handlerName,
    required this.handler,
    this.danger = AiToolDanger.low,
    this.requiresConfirmation = false,
  }) : namespaceId = _deriveNamespaceId(id);

  final String id;
  final String namespaceId;
  final String description;
  final String argsSchemaJson;
  final String returnSchemaJson;
  final AiToolDanger danger;
  final bool requiresConfirmation;
  final String handlerName;
  final AiToolHandler handler;

  JsonMap toSummaryJson() {
    return <String, dynamic>{
      'id': id,
      'namespaceId': namespaceId,
      'description': description,
      'danger': danger.wireValue,
      'requiresConfirmation': requiresConfirmation,
    };
  }

  JsonMap toFullJson() {
    return <String, dynamic>{
      'id': id,
      'namespaceId': namespaceId,
      'description': description,
      'argsSchemaJson': argsSchemaJson,
      'returnSchemaJson': returnSchemaJson,
      'danger': danger.wireValue,
      'requiresConfirmation': requiresConfirmation,
      'handlerName': handlerName,
    };
  }
}

class AiToolExecutionContext {
  AiToolExecutionContext({
    required this.config,
    required this.registry,
    required this.resultHandleStore,
    required this.runtimeState,
    required this.agentManual,
    required this.readToken,
    required this.regenerateToken,
    required this.buildHealthPayload,
    required this.buildAgentBriefPayload,
  });

  final AiFlutterAgentConfig config;
  final AiToolRegistry registry;
  final ResultHandleStore resultHandleStore;
  final AiRuntimeState runtimeState;
  final String agentManual;
  final String Function() readToken;
  final Future<String> Function() regenerateToken;
  final JsonMap Function({bool includeOk}) buildHealthPayload;
  final JsonMap Function({bool includeOk}) buildAgentBriefPayload;
}

class AiToolRegistry {
  AiToolRegistry({required List<AiManifestBundleDefinition> bundles}) : _bundles = bundles;

  final List<AiManifestBundleDefinition> _bundles;
  final Map<String, AiToolDefinition> _tools = <String, AiToolDefinition>{};
  DateTime _updatedAt = DateTime.now().toUtc();

  void register(AiToolDefinition tool) {
    if (_tools.containsKey(tool.id)) {
      throw StateError('Duplicate tool id: ${tool.id}');
    }
    _tools[tool.id] = tool;
    _updatedAt = DateTime.now().toUtc();
  }

  AiToolDefinition? findTool(String id) => _tools[id];

  int get count => _tools.length;

  List<AiToolDefinition> get tools {
    final list = _tools.values.toList();
    list.sort((left, right) => left.id.compareTo(right.id));
    return list;
  }

  List<JsonMap> get namespaceInfos {
    final counts = <String, int>{};
    for (final tool in _tools.values) {
      counts.update(tool.namespaceId, (value) => value + 1, ifAbsent: () => 1);
    }

    final namespaces = counts.entries
        .map((entry) => <String, dynamic>{'id': entry.key, 'count': entry.value})
        .toList();
    namespaces.sort((left, right) => (left['id'] as String).compareTo(right['id'] as String));
    return namespaces;
  }

  String get manifestHash {
    final buffer = StringBuffer()
      ..write(AiFlutterAgentConfig.protocolVersion)
      ..write('|');
    for (final tool in tools) {
      buffer
        ..write(tool.id)
        ..write('|')
        ..write(tool.namespaceId)
        ..write('|')
        ..write(tool.description)
        ..write('|')
        ..write(tool.argsSchemaJson)
        ..write('|')
        ..write(tool.returnSchemaJson)
        ..write('|')
        ..write(tool.danger.wireValue)
        ..write('|')
        ..write(tool.requiresConfirmation ? '1' : '0')
        ..write('|')
        ..write(tool.handlerName)
        ..write('\n');
    }
    var hash = BigInt.parse('1469598103934665603');
    final prime = BigInt.parse('1099511628211');
    final mask = (BigInt.one << 64) - BigInt.one;
    for (final codeUnit in buffer.toString().codeUnits) {
      hash ^= BigInt.from(codeUnit);
      hash = (hash * prime) & mask;
    }
    return hash.toRadixString(16).padLeft(16, '0');
  }

  JsonMap buildManifestSummary(AiFlutterAgentConfig config) {
    return <String, dynamic>{
      'framework': AiFlutterAgentConfig.frameworkName,
      'service': AiFlutterAgentConfig.serviceName,
      'serviceId': AiFlutterAgentConfig.serviceId,
      'version': config.serviceVersion,
      'platformId': AiFlutterAgentConfig.platformId,
      'protocolVersion': AiFlutterAgentConfig.protocolVersion,
      'manifestHash': manifestHash,
      'toolCount': count,
      'generatedAt': _updatedAt.toIso8601String(),
      'namespaces': namespaceInfos,
      'tools': tools.map((tool) => tool.toSummaryJson()).toList(),
    };
  }

  JsonMap buildManifestFull(AiFlutterAgentConfig config) {
    return <String, dynamic>{
      'framework': AiFlutterAgentConfig.frameworkName,
      'service': AiFlutterAgentConfig.serviceName,
      'serviceId': AiFlutterAgentConfig.serviceId,
      'version': config.serviceVersion,
      'platformId': AiFlutterAgentConfig.platformId,
      'protocolVersion': AiFlutterAgentConfig.protocolVersion,
      'manifestHash': manifestHash,
      'toolCount': count,
      'projectRoot': config.projectRoot,
      'serverUrl': config.serverUrl,
      'generatedAt': _updatedAt.toIso8601String(),
      'namespaces': namespaceInfos,
      'tools': tools.map((tool) => tool.toFullJson()).toList(),
    };
  }

  JsonMap buildManifestSearch(
    AiFlutterAgentConfig config, {
    String query = '',
    int limit = 0,
    String namespaceId = '',
    String bundleId = '',
  }) {
    final normalizedQuery = _normalizeQuery(query);
    final normalizedNamespace = namespaceId.trim().toLowerCase();
    final bundle = findBundle(bundleId);
    final safeLimit = limit <= 0 ? 8 : limit.clamp(1, 24).toInt();

    if (bundleId.isNotEmpty && bundle == null) {
      throw StateError('Unknown manifest bundle: $bundleId');
    }

    final hits = <JsonMap>[];
    for (final tool in tools) {
      if (normalizedNamespace.isNotEmpty && tool.namespaceId != normalizedNamespace) {
        continue;
      }
      if (bundle != null && !_matchesBundle(tool, bundle)) {
        continue;
      }

      final scoreResult = _scoreTool(tool, normalizedQuery);
      if (normalizedQuery.isNotEmpty && scoreResult.score <= 0) {
        continue;
      }
      final score = normalizedQuery.isEmpty ? 1 : scoreResult.score;
      hits.add(<String, dynamic>{
        'id': tool.id,
        'namespaceId': tool.namespaceId,
        'description': tool.description,
        'danger': tool.danger.wireValue,
        'requiresConfirmation': tool.requiresConfirmation,
        'score': score,
        'whyMatched': scoreResult.whyMatched.isNotEmpty
            ? scoreResult.whyMatched
            : normalizedNamespace.isNotEmpty
                ? 'namespace filter'
                : bundle != null
                    ? 'bundle filter'
                    : 'default listing',
      });
    }

    hits.sort((left, right) {
      final scoreCompare = (right['score'] as int).compareTo(left['score'] as int);
      if (scoreCompare != 0) {
        return scoreCompare;
      }
      return (left['id'] as String).compareTo(right['id'] as String);
    });

    return <String, dynamic>{
      'framework': AiFlutterAgentConfig.frameworkName,
      'protocolVersion': AiFlutterAgentConfig.protocolVersion,
      'serviceVersion': config.serviceVersion,
      'manifestHash': manifestHash,
      'query': query,
      'namespaceId': normalizedNamespace,
      'bundleId': bundle?.id ?? '',
      'limit': safeLimit,
      'totalMatches': hits.length,
      'items': hits.take(safeLimit).toList(),
    };
  }

  JsonMap buildDescribeMany(AiFlutterAgentConfig config, Iterable<String>? ids) {
    final found = <JsonMap>[];
    final missing = <String>[];
    final seen = <String>{};
    final requestedIds = ids?.toList() ?? const <String>[];

    for (final rawId in requestedIds) {
      final id = rawId.trim();
      if (id.isEmpty || !seen.add(id)) {
        continue;
      }
      final tool = _tools[id];
      if (tool == null) {
        missing.add(id);
      } else {
        found.add(tool.toFullJson());
      }
    }

    return <String, dynamic>{
      'framework': AiFlutterAgentConfig.frameworkName,
      'protocolVersion': AiFlutterAgentConfig.protocolVersion,
      'serviceVersion': config.serviceVersion,
      'manifestHash': manifestHash,
      'requestedCount': requestedIds.length,
      'returnedCount': found.length,
      'missingIds': missing,
      'tools': found,
    };
  }

  JsonMap buildBundleIndex(AiFlutterAgentConfig config) {
    final bundles = _bundles
        .map((bundle) => _buildBundleInfo(bundle))
        .where((item) => (item['toolCount'] as int) > 0)
        .toList();
    return <String, dynamic>{
      'framework': AiFlutterAgentConfig.frameworkName,
      'protocolVersion': AiFlutterAgentConfig.protocolVersion,
      'serviceVersion': config.serviceVersion,
      'manifestHash': manifestHash,
      'bundleCount': bundles.length,
      'bundles': bundles,
    };
  }

  JsonMap? tryBuildBundle(AiFlutterAgentConfig config, String bundleId) {
    final bundle = findBundle(bundleId);
    if (bundle == null) {
      return null;
    }

    final bundleTools = tools.where((tool) => _matchesBundle(tool, bundle)).toList();
    final namespaces = bundleTools.map((tool) => tool.namespaceId).toSet().toList()..sort();
    return <String, dynamic>{
      'framework': AiFlutterAgentConfig.frameworkName,
      'protocolVersion': AiFlutterAgentConfig.protocolVersion,
      'serviceVersion': config.serviceVersion,
      'manifestHash': manifestHash,
      'id': bundle.id,
      'description': bundle.description,
      'toolCount': bundleTools.length,
      'namespaces': namespaces,
      'tools': bundleTools.map((tool) => tool.toSummaryJson()).toList(),
    };
  }

  AiManifestBundleDefinition? findBundle(String? bundleId) {
    if (bundleId == null || bundleId.trim().isEmpty) {
      return null;
    }
    for (final bundle in _bundles) {
      if (bundle.id == bundleId.trim()) {
        return bundle;
      }
    }
    return null;
  }

  bool _matchesBundle(AiToolDefinition tool, AiManifestBundleDefinition bundle) {
    for (final prefix in bundle.prefixes) {
      if (tool.id.startsWith(prefix)) {
        return true;
      }
    }
    return false;
  }

  JsonMap _buildBundleInfo(AiManifestBundleDefinition bundle) {
    final namespaces = <String>{};
    var toolCount = 0;
    for (final tool in tools) {
      if (!_matchesBundle(tool, bundle)) {
        continue;
      }
      namespaces.add(tool.namespaceId);
      toolCount++;
    }
    final namespaceList = namespaces.toList()..sort();
    return <String, dynamic>{
      'id': bundle.id,
      'description': bundle.description,
      'toolCount': toolCount,
      'namespaces': namespaceList,
    };
  }

  _ScoreResult _scoreTool(AiToolDefinition tool, String normalizedQuery) {
    if (normalizedQuery.isEmpty) {
      return const _ScoreResult(score: 0, whyMatched: '');
    }

    final haystacks = <String, String>{
      'tool id': tool.id.toLowerCase(),
      'namespace': tool.namespaceId.toLowerCase(),
      'description': tool.description.toLowerCase(),
      'handler': tool.handlerName.toLowerCase(),
    };

    var score = 0;
    String whyMatched = '';
    for (final token in _tokenize(normalizedQuery)) {
      for (final entry in haystacks.entries) {
        if (!entry.value.contains(token)) {
          continue;
        }
        score += entry.key == 'tool id' ? 6 : entry.key == 'namespace' ? 4 : 2;
        whyMatched = whyMatched.isEmpty ? '${entry.key} matched "$token"' : whyMatched;
      }
    }

    return _ScoreResult(score: score, whyMatched: whyMatched);
  }
}

class _ScoreResult {
  const _ScoreResult({required this.score, required this.whyMatched});

  final int score;
  final String whyMatched;
}

String _deriveNamespaceId(String toolId) {
  final separator = toolId.indexOf('.');
  if (separator <= 0) {
    return toolId.trim().toLowerCase();
  }
  return toolId.substring(0, separator).trim().toLowerCase();
}

String _normalizeQuery(String query) => query.trim().toLowerCase();

List<String> _tokenize(String value) {
  return value
      .split(RegExp(r'[^a-z0-9_]+'))
      .where((token) => token.trim().isNotEmpty)
      .toList();
}

String _join(String left, String right) {
  final normalizedLeft = left.replaceAll('\\', '/').replaceAll(RegExp(r'/+$'), '');
  final normalizedRight = right.replaceAll('\\', '/').replaceAll(RegExp(r'^/+'), '');
  return '$normalizedLeft/$normalizedRight';
}

JsonMap ensureJsonMap(Object? value) {
  if (value == null) {
    return <String, dynamic>{};
  }
  if (value is JsonMap) {
    return value;
  }
  if (value is Map) {
    return value.map((key, entryValue) => MapEntry(key.toString(), entryValue));
  }
  throw const FormatException('Expected a JSON object.');
}

String encodePretty(Object? value) {
  const encoder = JsonEncoder.withIndent('  ');
  return encoder.convert(value);
}
