import 'dart:convert';
import 'dart:io';

import 'models.dart';

List<AiManifestBundleDefinition> defaultBundles() {
  return const <AiManifestBundleDefinition>[
    AiManifestBundleDefinition(
      id: 'project-inspection',
      description: 'Project metadata, file listing, file reading, text search, and widget indexing.',
      prefixes: <String>['project.', 'flutter.widget_'],
    ),
    AiManifestBundleDefinition(
      id: 'flutter-workflow',
      description: 'Flutter CLI workflows such as pub get, analyze, test, and doctor.',
      prefixes: <String>['flutter.'],
    ),
    AiManifestBundleDefinition(
      id: 'service-diagnostics',
      description: 'Health, manifest discovery, config, logs, and recent call inspection.',
      prefixes: <String>['system.', 'manifest.', 'tool.', 'agent.', 'service.'],
    ),
  ];
}

void registerDefaultTools(AiToolRegistry registry, AiToolExecutionContext context) {
  registry.register(
    AiToolDefinition(
      id: 'system.health',
      description: 'Returns service health metadata, manifestHash, token headers, and discovery hints.',
      argsSchemaJson: '{}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'systemHealth',
      handler: (args, toolContext) async => toolContext.buildHealthPayload(),
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'manifest.get',
      description: 'Returns the full manifest with complete schemas. Prefer manifest.get_summary or manifest.search unless full fallback is necessary.',
      argsSchemaJson: '{}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'getManifest',
      handler: (args, toolContext) async => toolContext.registry.buildManifestFull(toolContext.config),
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'manifest.get_summary',
      description: 'Returns the lightweight manifest summary without full schemas.',
      argsSchemaJson: '{}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'getManifestSummary',
      handler: (args, toolContext) async => toolContext.registry.buildManifestSummary(toolContext.config),
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'manifest.search',
      description: 'Searches the manifest for candidate tools. Args: query, limit, namespaceId, bundleId.',
      argsSchemaJson: '{"type":"object","properties":{"query":{"type":"string"},"limit":{"type":"integer"},"namespaceId":{"type":"string"},"bundleId":{"type":"string"}}}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'searchManifest',
      handler: (args, toolContext) async => toolContext.registry.buildManifestSearch(
        toolContext.config,
        query: args['query']?.toString() ?? '',
        limit: _intArg(args, 'limit', 0),
        namespaceId: args['namespaceId']?.toString() ?? '',
        bundleId: args['bundleId']?.toString() ?? '',
      ),
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'manifest.list_bundles',
      description: 'Lists focused manifest bundles for project inspection, Flutter workflows, and diagnostics.',
      argsSchemaJson: '{}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'listBundles',
      handler: (args, toolContext) async => toolContext.registry.buildBundleIndex(toolContext.config),
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'manifest.get_bundle',
      description: 'Returns the lightweight manifest for one focused capability bundle. Args: bundleId.',
      argsSchemaJson: '{"type":"object","properties":{"bundleId":{"type":"string"}},"required":["bundleId"]}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'getBundle',
      handler: (args, toolContext) async {
        final bundleId = _requiredString(args, 'bundleId');
        final bundle = toolContext.registry.tryBuildBundle(toolContext.config, bundleId);
        if (bundle == null) {
          throw StateError('Unknown manifest bundle: $bundleId');
        }
        return bundle;
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'tool.describe_many',
      description: 'Returns exact schemas and metadata for specific tool ids. Args: ids.',
      argsSchemaJson: '{"type":"object","properties":{"ids":{"type":"array","items":{"type":"string"}}}}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'describeMany',
      handler: (args, toolContext) async {
        final ids = (args['ids'] as List?)?.map((item) => item.toString());
        return toolContext.registry.buildDescribeMany(toolContext.config, ids);
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'agent.get_brief',
      description: 'Returns the concise protocol brief for capability discovery and paged result handling.',
      argsSchemaJson: '{}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'getAgentBrief',
      handler: (args, toolContext) async => toolContext.buildAgentBriefPayload(),
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'agent.get_manual',
      description: 'Returns the built-in AGENT.md AI operating manual.',
      argsSchemaJson: '{}',
      returnSchemaJson: '{"type":"object","properties":{"content":{"type":"string"}}}',
      handlerName: 'getAgentManual',
      handler: (args, toolContext) async => <String, dynamic>{
        'content': toolContext.agentManual,
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'service.config_get',
      description: 'Returns current service configuration without exposing the full token.',
      argsSchemaJson: '{}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'getServiceConfig',
      handler: (args, toolContext) async {
        final token = toolContext.readToken();
        return <String, dynamic>{
          'framework': AiFlutterAgentConfig.frameworkName,
          'serviceId': AiFlutterAgentConfig.serviceId,
          'platformId': AiFlutterAgentConfig.platformId,
          'requireToken': toolContext.config.requireToken,
          'fullAccessEnabled': toolContext.config.fullAccessEnabled,
          'acceptedTokenHeaders': toolContext.config.acceptedTokenHeaders,
          'supportsDynamicToolRegistration': false,
          'projectRoot': toolContext.config.projectRoot,
          'serverUrl': toolContext.config.serverUrl,
          'tokenPath': toolContext.config.tokenFilePath,
          'tokenPreview': _maskToken(token),
          'flutterExecutable': toolContext.config.flutterExecutable,
          'toolTimeoutMs': toolContext.config.toolTimeoutMs,
        };
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'service.log_recent',
      description: 'Returns recent internal service logs with a paged resultHandle when needed. Args: maxEntries, pageSize.',
      argsSchemaJson: '{"type":"object","properties":{"maxEntries":{"type":"integer"},"pageSize":{"type":"integer"}}}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'getRecentLogs',
      handler: (args, toolContext) async {
        final maxEntries = _intArg(args, 'maxEntries', 100);
        final pageSize = _intArg(args, 'pageSize', 20);
        final entries = toolContext.runtimeState.recentLogs(maxEntries);
        return toolContext.resultHandleStore.buildItemsResult(
          sourceToolId: 'service.log_recent',
          fieldName: 'entries',
          items: entries,
          summary: <String, dynamic>{
            'source': 'service.log_recent',
            'maxEntries': maxEntries,
          },
          pageSize: pageSize,
        );
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'service.call_recent',
      description: 'Returns recent AI tool calls with a paged resultHandle when needed. Args: maxEntries, pageSize.',
      argsSchemaJson: '{"type":"object","properties":{"maxEntries":{"type":"integer"},"pageSize":{"type":"integer"}}}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'getRecentCalls',
      handler: (args, toolContext) async {
        final maxEntries = _intArg(args, 'maxEntries', 100);
        final pageSize = _intArg(args, 'pageSize', 20);
        final entries = toolContext.runtimeState.recentCalls(maxEntries);
        return toolContext.resultHandleStore.buildItemsResult(
          sourceToolId: 'service.call_recent',
          fieldName: 'entries',
          items: entries,
          summary: <String, dynamic>{
            'source': 'service.call_recent',
            'maxEntries': maxEntries,
          },
          pageSize: pageSize,
        );
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'service.regenerate_token',
      description: 'Regenerates the local API token. Existing AI clients must reload the token file.',
      argsSchemaJson: '{}',
      returnSchemaJson: '{"type":"object","properties":{"tokenPath":{"type":"string"}}}',
      handlerName: 'regenerateToken',
      danger: AiToolDanger.high,
      requiresConfirmation: true,
      handler: (args, toolContext) async {
        await toolContext.regenerateToken();
        return <String, dynamic>{
          'tokenPath': toolContext.config.tokenFilePath,
        };
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'project.info',
      description: 'Returns Flutter project metadata derived from pubspec.yaml and common directories.',
      argsSchemaJson: '{}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'projectInfo',
      handler: (args, toolContext) async => _projectInfo(toolContext),
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'project.list_files',
      description: 'Lists project files under an optional subfolder with paging. Args: under, extensions, contains, maxEntries, pageSize.',
      argsSchemaJson: '{"type":"object","properties":{"under":{"type":"string"},"extensions":{"type":"array","items":{"type":"string"}},"contains":{"type":"string"},"maxEntries":{"type":"integer"},"pageSize":{"type":"integer"}}}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'listProjectFiles',
      handler: (args, toolContext) async {
        final under = args['under']?.toString() ?? '';
        final contains = args['contains']?.toString() ?? '';
        final maxEntries = _intArg(args, 'maxEntries', 300);
        final pageSize = _intArg(args, 'pageSize', 20);
        final extensions = (args['extensions'] as List?)
                ?.map((item) => item.toString().trim().toLowerCase())
                .where((item) => item.isNotEmpty)
                .toSet() ??
            <String>{};
        final rootDirectory = _resolveDirectory(toolContext.config, under);
        final items = <Object?>[];

        await for (final entity in rootDirectory.list(recursive: true, followLinks: false)) {
          if (entity is! File) {
            continue;
          }
          final relativePath = _relativeToProject(toolContext.config, entity.path);
          if (extensions.isNotEmpty && !extensions.any((ext) => relativePath.toLowerCase().endsWith(ext))) {
            continue;
          }
          if (contains.isNotEmpty && !relativePath.toLowerCase().contains(contains.toLowerCase())) {
            continue;
          }
          final stat = await entity.stat();
          items.add(<String, dynamic>{
            'path': relativePath,
            'sizeBytes': stat.size,
            'modifiedAt': stat.modified.toUtc().toIso8601String(),
          });
          if (items.length >= maxEntries) {
            break;
          }
        }

        return toolContext.resultHandleStore.buildItemsResult(
          sourceToolId: 'project.list_files',
          fieldName: 'items',
          items: items,
          summary: <String, dynamic>{
            'under': under,
            'contains': contains,
            'extensions': extensions.toList(),
            'maxEntries': maxEntries,
          },
          pageSize: pageSize,
        );
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'project.read_text',
      description: 'Reads a UTF-8 text file from the project with paged chunking. Args: path, offset, limit.',
      argsSchemaJson: '{"type":"object","properties":{"path":{"type":"string"},"offset":{"type":"integer"},"limit":{"type":"integer"}},"required":["path"]}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'readProjectText',
      handler: (args, toolContext) async {
        final relativePath = _requiredString(args, 'path');
        final offset = _intArg(args, 'offset', 0);
        final limit = _intArg(args, 'limit', 4096);
        final file = _resolveFile(toolContext.config, relativePath);
        if (!await file.exists()) {
          throw StateError('File not found: $relativePath');
        }
        final text = await file.readAsString();
        final stat = await file.stat();
        return toolContext.resultHandleStore.buildTextResult(
          sourceToolId: 'project.read_text',
          text: text,
          summary: <String, dynamic>{
            'path': relativePath,
            'sizeBytes': stat.size,
            'modifiedAt': stat.modified.toUtc().toIso8601String(),
          },
          offset: offset,
          length: limit,
        );
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'project.search_text',
      description: 'Searches UTF-8 text files for a substring. Args: query, under, extensions, maxMatches, pageSize.',
      argsSchemaJson: '{"type":"object","properties":{"query":{"type":"string"},"under":{"type":"string"},"extensions":{"type":"array","items":{"type":"string"}},"maxMatches":{"type":"integer"},"pageSize":{"type":"integer"}},"required":["query"]}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'searchProjectText',
      handler: (args, toolContext) async {
        final query = _requiredString(args, 'query');
        final under = args['under']?.toString() ?? 'lib';
        final pageSize = _intArg(args, 'pageSize', 20);
        final maxMatches = _intArg(args, 'maxMatches', 100);
        final extensions = (args['extensions'] as List?)
                ?.map((item) => item.toString().trim().toLowerCase())
                .where((item) => item.isNotEmpty)
                .toSet() ??
            <String>{'.dart', '.yaml', '.yml', '.json', '.md', '.txt'};
        final directory = _resolveDirectory(toolContext.config, under);
        final matches = <Object?>[];

        await for (final entity in directory.list(recursive: true, followLinks: false)) {
          if (entity is! File) {
            continue;
          }
          final relativePath = _relativeToProject(toolContext.config, entity.path);
          if (!extensions.any((ext) => relativePath.toLowerCase().endsWith(ext))) {
            continue;
          }
          String content;
          try {
            content = await entity.readAsString();
          } on FileSystemException {
            continue;
          }

          final lines = const LineSplitter().convert(content);
          for (var lineIndex = 0; lineIndex < lines.length; lineIndex++) {
            final line = lines[lineIndex];
            final column = line.toLowerCase().indexOf(query.toLowerCase());
            if (column < 0) {
              continue;
            }
            matches.add(<String, dynamic>{
              'path': relativePath,
              'line': lineIndex + 1,
              'column': column + 1,
              'snippet': line.trim(),
            });
            if (matches.length >= maxMatches) {
              break;
            }
          }
          if (matches.length >= maxMatches) {
            break;
          }
        }

        return toolContext.resultHandleStore.buildItemsResult(
          sourceToolId: 'project.search_text',
          fieldName: 'matches',
          items: matches,
          summary: <String, dynamic>{
            'query': query,
            'under': under,
            'maxMatches': maxMatches,
          },
          pageSize: pageSize,
        );
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'project.write_text',
      description: 'Writes a UTF-8 text file inside the project. Args: path, content, createDirectories.',
      argsSchemaJson: '{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"},"createDirectories":{"type":"boolean"}},"required":["path","content"]}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'writeProjectText',
      danger: AiToolDanger.high,
      requiresConfirmation: true,
      handler: (args, toolContext) async {
        final relativePath = _requiredString(args, 'path');
        final content = _requiredText(args, 'content');
        final createDirectories = _boolArg(args, 'createDirectories', true);
        final file = _resolveFile(toolContext.config, relativePath);
        if (createDirectories) {
          await file.parent.create(recursive: true);
        }
        await file.writeAsString(content);
        return <String, dynamic>{
          'path': relativePath,
          'sizeBytes': utf8.encode(content).length,
        };
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'project.delete_file',
      description: 'Deletes one file inside the project. Args: path.',
      argsSchemaJson: '{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'deleteProjectFile',
      danger: AiToolDanger.high,
      requiresConfirmation: true,
      handler: (args, toolContext) async {
        final relativePath = _requiredString(args, 'path');
        final file = _resolveFile(toolContext.config, relativePath);
        final existed = await file.exists();
        if (existed) {
          await file.delete();
        }
        return <String, dynamic>{
          'path': relativePath,
          'deleted': existed,
        };
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'flutter.pub_get',
      description: 'Runs flutter pub get in the configured project root.',
      argsSchemaJson: '{}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'flutterPubGet',
      handler: (args, toolContext) async => _runFlutterCommand(
        toolId: 'flutter.pub_get',
        subcommand: const <String>['pub', 'get'],
        toolContext: toolContext,
      ),
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'flutter.analyze',
      description: 'Runs flutter analyze in the configured project root.',
      argsSchemaJson: '{"type":"object","properties":{"fatalInfos":{"type":"boolean"},"fatalWarnings":{"type":"boolean"}}}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'flutterAnalyze',
      handler: (args, toolContext) async {
        final subcommand = <String>['analyze'];
        if (_boolArg(args, 'fatalInfos', false)) {
          subcommand.add('--fatal-infos');
        }
        if (_boolArg(args, 'fatalWarnings', false)) {
          subcommand.add('--fatal-warnings');
        }
        return _runFlutterCommand(
          toolId: 'flutter.analyze',
          subcommand: subcommand,
          toolContext: toolContext,
        );
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'flutter.test',
      description: 'Runs flutter test in the configured project root. Args: target, reporter, machine.',
      argsSchemaJson: '{"type":"object","properties":{"target":{"type":"string"},"reporter":{"type":"string"},"machine":{"type":"boolean"}}}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'flutterTest',
      handler: (args, toolContext) async {
        final subcommand = <String>['test'];
        final reporter = args['reporter']?.toString() ?? '';
        final target = args['target']?.toString() ?? '';
        if (_boolArg(args, 'machine', false)) {
          subcommand.add('--machine');
        }
        if (reporter.isNotEmpty) {
          subcommand..add('--reporter')..add(reporter);
        }
        if (target.isNotEmpty) {
          subcommand.add(target);
        }
        return _runFlutterCommand(
          toolId: 'flutter.test',
          subcommand: subcommand,
          toolContext: toolContext,
        );
      },
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'flutter.doctor',
      description: 'Runs flutter doctor -v and pages the output when needed.',
      argsSchemaJson: '{}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'flutterDoctor',
      handler: (args, toolContext) async => _runFlutterCommand(
        toolId: 'flutter.doctor',
        subcommand: const <String>['doctor', '-v'],
        toolContext: toolContext,
      ),
    ),
  );

  registry.register(
    AiToolDefinition(
      id: 'flutter.widget_index',
      description: 'Indexes common Flutter widget classes under lib/. Args: under, pageSize, maxEntries.',
      argsSchemaJson: '{"type":"object","properties":{"under":{"type":"string"},"pageSize":{"type":"integer"},"maxEntries":{"type":"integer"}}}',
      returnSchemaJson: '{"type":"object"}',
      handlerName: 'flutterWidgetIndex',
      handler: (args, toolContext) async {
        final under = args['under']?.toString() ?? 'lib';
        final pageSize = _intArg(args, 'pageSize', 20);
        final maxEntries = _intArg(args, 'maxEntries', 200);
        final directory = _resolveDirectory(toolContext.config, under);
        final regex = RegExp(
          r'class\s+([A-Za-z0-9_]+)\s+extends\s+(StatelessWidget|StatefulWidget|ConsumerWidget|HookWidget|ConsumerStatefulWidget)',
        );
        final widgets = <Object?>[];

        await for (final entity in directory.list(recursive: true, followLinks: false)) {
          if (entity is! File || !entity.path.toLowerCase().endsWith('.dart')) {
            continue;
          }
          final relativePath = _relativeToProject(toolContext.config, entity.path);
          String content;
          try {
            content = await entity.readAsString();
          } on FileSystemException {
            continue;
          }
          final lines = const LineSplitter().convert(content);
          for (var lineIndex = 0; lineIndex < lines.length; lineIndex++) {
            final match = regex.firstMatch(lines[lineIndex]);
            if (match == null) {
              continue;
            }
            widgets.add(<String, dynamic>{
              'className': match.group(1),
              'widgetBase': match.group(2),
              'path': relativePath,
              'line': lineIndex + 1,
            });
            if (widgets.length >= maxEntries) {
              break;
            }
          }
          if (widgets.length >= maxEntries) {
            break;
          }
        }

        return toolContext.resultHandleStore.buildItemsResult(
          sourceToolId: 'flutter.widget_index',
          fieldName: 'items',
          items: widgets,
          summary: <String, dynamic>{
            'under': under,
            'maxEntries': maxEntries,
          },
          pageSize: pageSize,
        );
      },
    ),
  );
}

Future<JsonMap> _projectInfo(AiToolExecutionContext context) async {
  final projectDirectory = Directory(context.config.projectRoot);
  final pubspecFile = File(_join(context.config.projectRoot, 'pubspec.yaml'));
  final hasPubspec = await pubspecFile.exists();
  final pubspec = hasPubspec ? await pubspecFile.readAsString() : '';
  return <String, dynamic>{
    'projectRoot': projectDirectory.absolute.path.replaceAll('\\', '/'),
    'hasPubspec': hasPubspec,
    'pubspecPath': hasPubspec ? 'pubspec.yaml' : '',
    'projectName': _yamlScalar(pubspec, 'name'),
    'description': _yamlScalar(pubspec, 'description'),
    'sdkConstraint': _yamlNestedScalar(pubspec, 'environment', 'sdk'),
    'hasLibDirectory': await Directory(_join(context.config.projectRoot, 'lib')).exists(),
    'hasTestDirectory': await Directory(_join(context.config.projectRoot, 'test')).exists(),
    'hasAndroidDirectory': await Directory(_join(context.config.projectRoot, 'android')).exists(),
    'hasIosDirectory': await Directory(_join(context.config.projectRoot, 'ios')).exists(),
  };
}

Future<JsonMap> _runFlutterCommand({
  required String toolId,
  required List<String> subcommand,
  required AiToolExecutionContext toolContext,
}) async {
  final result = await Process.run(
    toolContext.config.flutterExecutable,
    subcommand,
    workingDirectory: toolContext.config.projectRoot,
    runInShell: true,
  ).timeout(Duration(milliseconds: toolContext.config.toolTimeoutMs));

  final stdoutText = result.stdout?.toString() ?? '';
  final stderrText = result.stderr?.toString() ?? '';
  final combined = _combineProcessOutput(stdoutText, stderrText);
  final payload = toolContext.resultHandleStore.buildTextResult(
    sourceToolId: toolId,
    text: combined,
    summary: <String, dynamic>{
      'command': <String>[toolContext.config.flutterExecutable, ...subcommand],
      'exitCode': result.exitCode,
    },
  );
  payload['command'] = <String>[toolContext.config.flutterExecutable, ...subcommand];
  payload['exitCode'] = result.exitCode;
  payload['stdoutChars'] = stdoutText.length;
  payload['stderrChars'] = stderrText.length;
  return payload;
}

String _combineProcessOutput(String stdoutText, String stderrText) {
  if (stdoutText.isEmpty && stderrText.isEmpty) {
    return '';
  }
  if (stderrText.isEmpty) {
    return stdoutText;
  }
  if (stdoutText.isEmpty) {
    return stderrText;
  }
  return 'STDOUT:\n$stdoutText\n\nSTDERR:\n$stderrText';
}

String _maskToken(String token) {
  if (token.length < 8) {
    return '';
  }
  return '${token.substring(0, 4)}...${token.substring(token.length - 4)}';
}

int _intArg(Map<String, dynamic> args, String key, int defaultValue) {
  final value = args[key];
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

bool _boolArg(Map<String, dynamic> args, String key, bool defaultValue) {
  final value = args[key];
  if (value is bool) {
    return value;
  }
  if (value is String) {
    final normalized = value.trim().toLowerCase();
    if (normalized == 'true') {
      return true;
    }
    if (normalized == 'false') {
      return false;
    }
  }
  return defaultValue;
}

String _requiredString(Map<String, dynamic> args, String key) {
  final value = args[key]?.toString().trim() ?? '';
  if (value.isEmpty) {
    throw StateError('$key is required.');
  }
  return value;
}

String _requiredText(Map<String, dynamic> args, String key) {
  if (!args.containsKey(key) || args[key] == null) {
    throw StateError('$key is required.');
  }
  return args[key].toString();
}

Directory _resolveDirectory(AiFlutterAgentConfig config, String relativePath) {
  final path = relativePath.trim().isEmpty ? config.projectRoot : _resolveProjectPath(config, relativePath);
  final directory = Directory(path);
  if (!directory.existsSync()) {
    throw StateError('Directory not found: ${relativePath.trim().isEmpty ? "." : relativePath}');
  }
  return directory;
}

File _resolveFile(AiFlutterAgentConfig config, String relativePath) {
  return File(_resolveProjectPath(config, relativePath));
}

String _resolveProjectPath(AiFlutterAgentConfig config, String relativePath) {
  final sanitizedRoot = _normalizePath(config.projectRoot);
  final rawPath = relativePath.replaceAll('\\', '/').trim();
  if (rawPath.isEmpty) {
    return sanitizedRoot;
  }
  final candidate = rawPath.startsWith('/')
      ? _normalizePath(rawPath)
      : _normalizePath(_join(sanitizedRoot, rawPath));
  if (candidate != sanitizedRoot && !candidate.startsWith('$sanitizedRoot/')) {
    throw StateError('Path escapes the Flutter project root: $relativePath');
  }
  return candidate;
}

String _relativeToProject(AiFlutterAgentConfig config, String absolutePath) {
  final root = _normalizePath(config.projectRoot);
  final candidate = _normalizePath(absolutePath);
  if (candidate == root) {
    return '.';
  }
  if (candidate.startsWith('$root/')) {
    return candidate.substring(root.length + 1);
  }
  return candidate;
}

String _normalizePath(String path) {
  final replaced = path.replaceAll('\\', '/');
  final hasDrive = RegExp(r'^[A-Za-z]:').hasMatch(replaced);
  final drivePrefix = hasDrive ? replaced.substring(0, 2) : '';
  final pathWithoutDrive = hasDrive ? replaced.substring(2) : replaced;
  final isAbsolute = pathWithoutDrive.startsWith('/');
  final segments = pathWithoutDrive.split('/');
  final stack = <String>[];
  for (final segment in segments) {
    if (segment.isEmpty || segment == '.') {
      continue;
    }
    if (segment == '..') {
      if (stack.isNotEmpty && stack.last != '..') {
        stack.removeLast();
      } else if (!isAbsolute) {
        stack.add(segment);
      }
      continue;
    }
    stack.add(segment);
  }
  final prefix = hasDrive
      ? '$drivePrefix${isAbsolute ? '/' : ''}'
      : isAbsolute
          ? '/'
          : '';
  final normalized = '$prefix${stack.join('/')}';
  if (normalized.isEmpty) {
    return isAbsolute ? '/' : '.';
  }
  return normalized.replaceAll(RegExp(r'/+$'), '');
}

String _join(String left, String right) {
  final normalizedLeft = left.replaceAll('\\', '/').replaceAll(RegExp(r'/+$'), '');
  final normalizedRight = right.replaceAll('\\', '/').replaceAll(RegExp(r'^/+'), '');
  return '$normalizedLeft/$normalizedRight';
}

String _yamlScalar(String content, String key) {
  final pattern = RegExp('^${RegExp.escape(key)}\\s*:\\s*(.+)\$', multiLine: true);
  final match = pattern.firstMatch(content);
  return match == null ? '' : match.group(1)!.trim().replaceAll('"', '').replaceAll("'", '');
}

String _yamlNestedScalar(String content, String parentKey, String key) {
  final parentPattern = RegExp('^${RegExp.escape(parentKey)}\\s*:\\s*\$', multiLine: true);
  final parentMatch = parentPattern.firstMatch(content);
  if (parentMatch == null) {
    return '';
  }
  final rest = content.substring(parentMatch.end);
  final childPattern = RegExp(r'^\s{2,}' + RegExp.escape(key) + r'\s*:\s*(.+)$', multiLine: true);
  final childMatch = childPattern.firstMatch(rest);
  return childMatch == null ? '' : childMatch.group(1)!.trim().replaceAll('"', '').replaceAll("'", '');
}
