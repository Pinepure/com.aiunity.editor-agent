import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'generated_tool_host.dart';
import 'models.dart';
import 'result_handle_store.dart';
import 'tools.dart';

class AiFlutterAgentServer {
  AiFlutterAgentServer(this.config)
      : runtimeState = AiRuntimeState(),
        resultHandleStore = ResultHandleStore(),
        registry = AiToolRegistry(bundles: defaultBundles()),
        generatedToolHost = AiFlutterGeneratedToolHost(
          config: config,
        );

  final AiFlutterAgentConfig config;
  final AiRuntimeState runtimeState;
  final ResultHandleStore resultHandleStore;
  final AiToolRegistry registry;
  final AiFlutterGeneratedToolHost generatedToolHost;

  HttpServer? _server;
  StreamSubscription<FileSystemEvent>? _generatedToolsWatcher;
  bool _generatedToolsDirty = true;
  String _generatedToolsFingerprint = '';
  int _generatedToolsCount = 0;
  late final AiToolExecutionContext _toolContext = AiToolExecutionContext(
    config: config,
    registry: registry,
    resultHandleStore: resultHandleStore,
    runtimeState: runtimeState,
    agentManual: _agentManual,
    readToken: () => _token,
    regenerateToken: _regenerateToken,
    buildHealthPayload: ({bool includeOk = false}) => _buildHealthPayload(includeOk: includeOk),
    buildAgentBriefPayload: ({bool includeOk = false}) => _buildAgentBriefPayload(includeOk: includeOk),
    generatedToolHost: generatedToolHost,
    reloadGeneratedTools: ({bool force = false}) => _refreshRegistry(force: force),
  );
  late final String _agentManual = _loadAgentManual();
  String _token = '';

  bool get isRunning => _server != null;

  Future<void> start() async {
    if (isRunning) {
      return;
    }
    await generatedToolHost.ensureLayout();
    await _refreshRegistry(force: true);
    if (config.requireToken) {
      _token = await _ensureToken();
    }
    _server = await HttpServer.bind(config.host, config.port);
    _generatedToolsWatcher = Directory(config.generatedToolsDirectoryPath)
        .watch()
        .listen(
          (event) => markGeneratedToolsDirty('file event: ${event.type}'),
          onError: (Object error) => runtimeState.log(
            'warning',
            'Generated tool watcher error: $error',
          ),
        );
    runtimeState.log('info', 'Service started at ${config.serverUrl}');
    unawaited(_server!.forEach(_handleRequest));
  }

  Future<void> stop() async {
    if (_server == null) {
      return;
    }
    runtimeState.log('info', 'Service stopping.');
    await _generatedToolsWatcher?.cancel();
    _generatedToolsWatcher = null;
    await _server!.close(force: true);
    _server = null;
  }

  void markGeneratedToolsDirty([String reason = '']) {
    _generatedToolsDirty = true;
    if (reason.trim().isNotEmpty) {
      runtimeState.log('info', 'Generated tool registry marked dirty: $reason');
    }
  }

  Future<void> _handleRequest(HttpRequest request) async {
    _addCommonHeaders(request.response);

    if (request.method == 'OPTIONS') {
      request.response
        ..statusCode = HttpStatus.noContent
        ..close();
      return;
    }

    try {
      await _refreshRegistry();
      final path = request.uri.pathSegments.join('/');
      if (path == 'health' && request.method == 'GET') {
        await _sendJson(request.response, HttpStatus.ok, _buildHealthPayload(includeOk: true));
        return;
      }

      if (!_isAuthorized(request)) {
        await _sendError(
          request.response,
          HttpStatus.unauthorized,
          'Unauthorized. Provide ${AiFlutterAgentConfig.primaryTokenHeader} or ${AiFlutterAgentConfig.legacyTokenHeader}.',
        );
        return;
      }

      if ((path == 'manifest' || path == 'manifest/summary') && request.method == 'GET') {
        final detail = request.uri.queryParameters['detail'] ?? '';
        final payload = detail.toLowerCase() == 'full'
            ? registry.buildManifestFull(config)
            : registry.buildManifestSummary(config);
        await _sendJson(request.response, HttpStatus.ok, payload);
        return;
      }

      if (path == 'manifest/full' && request.method == 'GET') {
        await _sendJson(request.response, HttpStatus.ok, registry.buildManifestFull(config));
        return;
      }

      if (path == 'manifest/bundles' && request.method == 'GET') {
        await _sendJson(request.response, HttpStatus.ok, registry.buildBundleIndex(config));
        return;
      }

      if (path.startsWith('manifest/bundle/') && request.method == 'GET') {
        final bundleId = Uri.decodeComponent(path.substring('manifest/bundle/'.length));
        final payload = registry.tryBuildBundle(config, bundleId);
        if (payload == null) {
          await _sendError(request.response, HttpStatus.notFound, 'Unknown manifest bundle: $bundleId');
          return;
        }
        await _sendJson(request.response, HttpStatus.ok, payload);
        return;
      }

      if (path == 'manifest/search' && request.method == 'POST') {
        final body = await _readBodyAsJson(request);
        final payload = registry.buildManifestSearch(
          config,
          query: body['query']?.toString() ?? '',
          limit: _parseInt(body['limit'], 0),
          namespaceId: body['namespaceId']?.toString() ?? '',
          bundleId: body['bundleId']?.toString() ?? '',
        );
        await _sendJson(request.response, HttpStatus.ok, payload);
        return;
      }

      if (path == 'tool/describe_many' && request.method == 'POST') {
        final body = await _readBodyAsJson(request);
        final ids = (body['ids'] as List?)?.map((item) => item.toString());
        await _sendJson(request.response, HttpStatus.ok, registry.buildDescribeMany(config, ids));
        return;
      }

      if (path == 'agent/brief' && request.method == 'GET') {
        await _sendJson(request.response, HttpStatus.ok, _buildAgentBriefPayload(includeOk: true));
        return;
      }

      if (path == 'agent' && request.method == 'GET') {
        await _sendJson(request.response, HttpStatus.ok, <String, dynamic>{
          'ok': true,
          'content': _agentManual,
        });
        return;
      }

      if (path.startsWith('result/') && request.method == 'GET') {
        final handleId = Uri.decodeComponent(path.substring('result/'.length));
        final payload = resultHandleStore.buildPage(
          handleId,
          offset: _parseInt(request.uri.queryParameters['offset'], 0),
          limit: _parseInt(request.uri.queryParameters['limit'], 0),
        );
        if (payload == null) {
          await _sendError(request.response, HttpStatus.notFound, 'Unknown result handle: $handleId');
          return;
        }
        await _sendJson(request.response, HttpStatus.ok, payload);
        return;
      }

      if (path.startsWith('call/') && request.method == 'POST') {
        final toolId = Uri.decodeComponent(path.substring('call/'.length));
        final body = await _readBodyAsJson(request);
        final tool = registry.findTool(toolId);
        if (tool == null) {
          await _sendJson(
            request.response,
            HttpStatus.notFound,
            <String, dynamic>{
              'ok': false,
              'toolId': toolId,
              'error': 'Unknown tool: $toolId',
            },
          );
          return;
        }

        if (tool.requiresConfirmation && !config.fullAccessEnabled) {
          await _sendJson(
            request.response,
            HttpStatus.forbidden,
            <String, dynamic>{
              'ok': false,
              'toolId': toolId,
              'error': 'High-risk tool requires --full-access for this adapter.',
            },
          );
          return;
        }

        final watch = Stopwatch()..start();
        try {
          final result = await tool.handler(body, _toolContext)
              .timeout(Duration(milliseconds: config.toolTimeoutMs));
          runtimeState.recordCall(
            toolId: toolId,
            ok: true,
            durationMs: watch.elapsedMilliseconds,
            message: 'ok',
          );
          await _sendJson(
            request.response,
            HttpStatus.ok,
            <String, dynamic>{
              'ok': true,
              'toolId': toolId,
              'durationMs': watch.elapsedMilliseconds,
              'result': result,
            },
          );
        } catch (error) {
          runtimeState.recordCall(
            toolId: toolId,
            ok: false,
            durationMs: watch.elapsedMilliseconds,
            message: error.toString(),
          );
          await _sendJson(
            request.response,
            HttpStatus.internalServerError,
            <String, dynamic>{
              'ok': false,
              'toolId': toolId,
              'durationMs': watch.elapsedMilliseconds,
              'error': error.toString(),
            },
          );
        }
        return;
      }

      await _sendError(request.response, HttpStatus.notFound, 'Not found: $path');
    } catch (error) {
      runtimeState.log('error', error.toString());
      await _sendError(request.response, HttpStatus.internalServerError, error.toString());
    }
  }

  bool _isAuthorized(HttpRequest request) {
    if (!config.requireToken) {
      return true;
    }
    final provided = request.headers.value(AiFlutterAgentConfig.primaryTokenHeader) ??
        request.headers.value(AiFlutterAgentConfig.legacyTokenHeader);
    return provided != null && provided == _token;
  }

  Future<JsonMap> _readBodyAsJson(HttpRequest request) async {
    final content = await utf8.decoder.bind(request).join();
    if (content.trim().isEmpty) {
      return <String, dynamic>{};
    }
    final decoded = jsonDecode(content);
    return ensureJsonMap(decoded);
  }

  JsonMap _buildHealthPayload({bool includeOk = false}) {
    return <String, dynamic>{
      if (includeOk) 'ok': true,
      'framework': AiFlutterAgentConfig.frameworkName,
      'service': AiFlutterAgentConfig.serviceName,
      'serviceId': AiFlutterAgentConfig.serviceId,
      'version': config.serviceVersion,
      'platformId': AiFlutterAgentConfig.platformId,
      'protocolVersion': AiFlutterAgentConfig.protocolVersion,
      'serverRunning': isRunning,
      'requiresToken': config.requireToken,
      'acceptedTokenHeaders': config.acceptedTokenHeaders,
      'serverUrl': config.serverUrl,
      'manifestHash': registry.manifestHash,
      'toolCount': registry.count,
      'namespaces': registry.namespaceInfos,
      'supportsManifestSearch': true,
      'supportsDescribeMany': true,
      'supportsResultHandles': true,
      'supportsBundles': true,
      'supportsTextChunking': true,
      'supportsDynamicToolRegistration': true,
      'recommendedFlow': <String>[
        'GET /health and compare manifestHash before refreshing capabilities.',
        'Use POST /manifest/search or GET /manifest/bundle/{id} to narrow candidate tools.',
        'Use POST /tool/describe_many for exact argument and return schemas before calling tools.',
        'Use GET /result/{handleId} for additional pages or text chunks when a tool returns a resultHandle.',
        'Use GET /manifest/full only as a fallback when search is insufficient.',
      ],
      'paths': <String, dynamic>{
        'health': '/health',
        'manifestSummary': '/manifest',
        'manifestFull': '/manifest/full',
        'manifestSearch': '/manifest/search',
        'manifestBundles': '/manifest/bundles',
        'toolDescribeMany': '/tool/describe_many',
        'call': '/call/{toolId}',
        'agent': '/agent',
        'agentBrief': '/agent/brief',
        'resultPage': '/result/{handleId}',
      },
      'platform': <String, dynamic>{
        'projectRoot': config.projectRoot,
        'flutterExecutable': config.flutterExecutable,
        'dartExecutable': config.dartExecutable,
        'generatedToolsDirectoryPath': config.generatedToolsDirectoryPath,
        'generatedToolRuntimeDirectoryPath': config.generatedToolRuntimeDirectoryPath,
        'generatedToolCount': _generatedToolsCount,
      },
    };
  }

  JsonMap _buildAgentBriefPayload({bool includeOk = false}) {
    return <String, dynamic>{
      if (includeOk) 'ok': true,
      'framework': AiFlutterAgentConfig.frameworkName,
      'platformId': AiFlutterAgentConfig.platformId,
      'summary': 'Prefer cached discovery through health, manifest search, on-demand tool descriptions, and paged result handles instead of repeatedly loading the full manifest.',
      'steps': <String>[
        'Call GET /health and reuse cached capabilities while manifestHash stays unchanged.',
        'Search tools with POST /manifest/search or load a focused bundle with GET /manifest/bundle/{id}.',
        'Request exact tool schemas with POST /tool/describe_many before calling unfamiliar tools.',
        'When a tool returns resultHandle, page additional data through GET /result/{handleId} instead of re-running the tool with larger limits.',
        'Fallback to GET /manifest/full or GET /agent only when the optimized discovery flow is insufficient.',
      ],
      'paths': <String, dynamic>{
        'health': '/health',
        'manifestSummary': '/manifest',
        'manifestFull': '/manifest/full',
        'manifestSearch': '/manifest/search',
        'manifestBundles': '/manifest/bundles',
        'toolDescribeMany': '/tool/describe_many',
        'call': '/call/{toolId}',
        'agent': '/agent',
        'agentBrief': '/agent/brief',
        'resultPage': '/result/{handleId}',
      },
    };
  }

  Future<String> _ensureToken() async {
    final tokenFile = File(config.tokenFilePath);
    await tokenFile.parent.create(recursive: true);
    if (await tokenFile.exists()) {
      final existing = (await tokenFile.readAsString()).trim();
      if (existing.isNotEmpty) {
        return existing;
      }
    }
    return _regenerateToken();
  }

  Future<String> _regenerateToken() async {
    final token = _generateToken();
    final tokenFile = File(config.tokenFilePath);
    await tokenFile.parent.create(recursive: true);
    await tokenFile.writeAsString(token);
    _token = token;
    runtimeState.log('info', 'Token regenerated.');
    return token;
  }

  String _generateToken() {
    final random = Random.secure();
    final buffer = StringBuffer();
    for (var index = 0; index < 32; index++) {
      buffer.write(random.nextInt(16).toRadixString(16));
    }
    return buffer.toString();
  }

  String _loadAgentManual() {
    try {
      final manualUri = Platform.script.resolve('../AGENT.md');
      final file = File.fromUri(manualUri);
      if (file.existsSync()) {
        return file.readAsStringSync();
      }
    } catch (_) {
    }
    return '''
# AI Flutter Agent Protocol

Use GET /health, cache manifestHash, prefer manifest search or bundle loading, then use tool/describe_many before calling unfamiliar tools.
''';
  }

  void _addCommonHeaders(HttpResponse response) {
    response.headers
      ..set('Access-Control-Allow-Origin', 'http://127.0.0.1')
      ..set('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
      ..set(
        'Access-Control-Allow-Headers',
        'Content-Type, ${AiFlutterAgentConfig.primaryTokenHeader}, ${AiFlutterAgentConfig.legacyTokenHeader}',
      )
      ..set('Cache-Control', 'no-store')
      ..contentType = ContentType.json;
  }

  Future<void> _sendError(HttpResponse response, int statusCode, String message) {
    return _sendJson(response, statusCode, <String, dynamic>{'ok': false, 'error': message});
  }

  Future<void> _sendJson(HttpResponse response, int statusCode, Object? payload) async {
    response.statusCode = statusCode;
    response.write(jsonEncode(payload));
    await response.close();
  }

  Future<JsonMap> _refreshRegistry({bool force = false}) async {
    if (!force && !_generatedToolsDirty && registry.count > 0) {
      return <String, dynamic>{
        'reloaded': false,
        'manifestHash': registry.manifestHash,
        'generatedToolCount': _generatedToolsCount,
      };
    }

    final fingerprint = await generatedToolHost.computeFingerprint();
    if (!force && fingerprint == _generatedToolsFingerprint && registry.count > 0) {
      _generatedToolsDirty = false;
      return <String, dynamic>{
        'reloaded': false,
        'manifestHash': registry.manifestHash,
        'generatedToolCount': _generatedToolsCount,
      };
    }

    registry.reset();
    _toolContext.registry = registry;
    registerDefaultTools(registry, _toolContext);
    final generatedDefinitions = await generatedToolHost.loadDefinitions();
    for (final definition in generatedDefinitions) {
      registry.register(generatedToolHost.createToolDefinition(definition, _toolContext));
    }
    _generatedToolsFingerprint = fingerprint;
    _generatedToolsDirty = false;
    _generatedToolsCount = generatedDefinitions.length;
    runtimeState.log(
      'info',
      'Generated tool registry refreshed. Loaded ${generatedDefinitions.length} generated tool(s).',
    );
    return <String, dynamic>{
      'reloaded': true,
      'manifestHash': registry.manifestHash,
      'generatedToolCount': generatedDefinitions.length,
    };
  }
}

int _parseInt(Object? value, int defaultValue) {
  if (value is int) {
    return value;
  }
  if (value is num) {
    return value.toInt();
  }
  if (value is String) {
    return int.tryParse(value) ?? defaultValue;
  }
  return defaultValue;
}
