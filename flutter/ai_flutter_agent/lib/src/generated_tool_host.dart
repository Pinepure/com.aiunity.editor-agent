import 'dart:convert';
import 'dart:io';

import 'models.dart';

class AiFlutterGeneratedToolHost {
  AiFlutterGeneratedToolHost({
    required this.config,
  });

  final AiFlutterAgentConfig config;

  Directory get definitionsDirectory => Directory(config.generatedToolsDirectoryPath);
  Directory get runtimeDirectory => Directory(config.generatedToolRuntimeDirectoryPath);

  Future<void> ensureLayout() async {
    await definitionsDirectory.create(recursive: true);
    await runtimeDirectory.create(recursive: true);
  }

  Future<String> computeFingerprint() async {
    await ensureLayout();
    final files = await _listDefinitionFiles();
    final buffer = StringBuffer();
    for (final file in files) {
      buffer
        ..writeln(_basename(file.path))
        ..writeln(await file.readAsString());
      buffer.writeln('---');
    }
    return buffer.toString();
  }

  Future<List<JsonMap>> loadDefinitions() async {
    await ensureLayout();
    final definitions = <JsonMap>[];
    for (final file in await _listDefinitionFiles()) {
      final definition = await readDefinition(_basename(file.path));
      definitions.add(<String, dynamic>{
        ...definition,
        'fileName': _basename(file.path),
      });
    }
    return definitions;
  }

  Future<List<JsonMap>> listDefinitions() async {
    final definitions = await loadDefinitions();
    return definitions
        .map(
          (definition) => <String, dynamic>{
            'fileName': definition['fileName'],
            'toolId': definition['toolId'],
            'description': definition['description'],
            'danger': definition['danger'],
            'requiresConfirmation': definition['requiresConfirmation'] == true,
          },
        )
        .toList();
  }

  JsonMap getTemplate({
    String toolId = 'generated.flutter_example',
    String description = 'Generated Flutter tool.',
    String fileName = '',
  }) {
    final safeToolId = toolId.trim().isEmpty ? 'generated.flutter_example' : toolId.trim();
    return <String, dynamic>{
      'fileName': _normalizeFileName(fileName.trim().isEmpty ? _suggestedFileName(safeToolId) : fileName),
      'definition': <String, dynamic>{
        'toolId': safeToolId,
        'description': description.trim().isEmpty ? 'Generated Flutter tool.' : description.trim(),
        'danger': 'low',
        'requiresConfirmation': false,
        'argsSchema': <String, dynamic>{
          'type': 'object',
          'additionalProperties': false,
          'properties': <String, dynamic>{
            'path': <String, dynamic>{'type': 'string'},
          },
        },
        'returnSchema': <String, dynamic>{
          'type': 'object',
          'additionalProperties': true,
        },
        'source': '''
final info = await host.projectInfo();
final path = (args['path']?.toString().trim().isEmpty ?? true) ? 'pubspec.yaml' : args['path'].toString().trim();
final content = await host.readText(path);
return {
  'projectName': info['projectName'],
  'path': path,
  'contentPreview': content.length <= 400 ? content : content.substring(0, 400),
};''',
      },
      'notes': <String>[
        'The source body runs as async Dart inside a generated runner script with (args, host).',
        'Use host.readText, host.writeText, host.searchText, host.listFiles, host.runFlutter, host.runDart, and host.runCommand for project and runtime control.',
        'Return plain JSON-serializable maps, lists, strings, numbers, booleans, or null.',
        'Set requiresConfirmation to true for high-risk generated tools that mutate files or execute impactful commands.',
      ],
    };
  }

  Future<JsonMap> upsertDefinition({
    required String fileName,
    required Object? definition,
    bool validate = true,
  }) async {
    await ensureLayout();
    final safeName = _normalizeFileName(fileName);
    final normalized = _normalizeDefinition(definition);
    final runnerFile = await _writeRunnerFile(
      fileName: safeName,
      definition: normalized,
    );
    JsonMap? validation;
    if (validate) {
      validation = await _validateRunner(runnerFile);
    }
    final definitionFile = File(_join(config.generatedToolsDirectoryPath, safeName));
    await definitionFile.writeAsString('${encodePretty(normalized)}\n');
    return <String, dynamic>{
      'fileName': safeName,
      'path': definitionFile.path,
      'runnerPath': runnerFile.path,
      if (validation != null) 'validation': validation,
    };
  }

  Future<JsonMap> deleteDefinition(String fileName) async {
    await ensureLayout();
    final safeName = _normalizeFileName(fileName);
    final definitionFile = File(_join(config.generatedToolsDirectoryPath, safeName));
    final runnerFile = _runnerFileFor(safeName);
    final deleted = await definitionFile.exists();
    if (deleted) {
      await definitionFile.delete();
    }
    if (await runnerFile.exists()) {
      await runnerFile.delete();
    }
    return <String, dynamic>{
      'deleted': deleted,
      'fileName': safeName,
    };
  }

  Future<JsonMap> readDefinition(String fileName) async {
    final safeName = _normalizeFileName(fileName);
    final file = File(_join(config.generatedToolsDirectoryPath, safeName));
    if (!await file.exists()) {
      throw StateError('Generated tool definition not found: $safeName');
    }
    final raw = jsonDecode(await file.readAsString());
    return _normalizeDefinition(raw);
  }

  AiToolDefinition createToolDefinition(JsonMap definition, AiToolExecutionContext context) {
    return AiToolDefinition(
      id: definition['toolId']!.toString(),
      description: definition['description']!.toString(),
      argsSchemaJson: jsonEncode(definition['argsSchema']),
      returnSchemaJson: jsonEncode(definition['returnSchema']),
      handlerName: 'generated:${definition['fileName']}',
      danger: _dangerFromString(definition['danger']?.toString() ?? 'low'),
      requiresConfirmation: definition['requiresConfirmation'] == true,
      handler: (args, toolContext) => executeDefinition(definition, args, toolContext),
    );
  }

  Future<Object?> executeDefinition(
    JsonMap definition,
    JsonMap args,
    AiToolExecutionContext context,
  ) async {
    await ensureLayout();
    final fileName = _normalizeFileName(definition['fileName']?.toString() ?? _suggestedFileName(definition['toolId']!.toString()));
    final runnerFile = await _writeRunnerFile(
      fileName: fileName,
      definition: definition,
    );

    final payload = await Process.run(
      config.dartExecutable,
      <String>[runnerFile.path],
      workingDirectory: config.projectRoot,
      runInShell: true,
      environment: <String, String>{
        'AI_FLUTTER_AGENT_ARGS': base64Encode(utf8.encode(jsonEncode(args))),
        'AI_FLUTTER_AGENT_PROJECT_ROOT': config.projectRoot,
        'AI_FLUTTER_AGENT_STATE_DIR': config.stateDirectoryPath,
        'AI_FLUTTER_AGENT_GENERATED_TOOLS_DIR': config.generatedToolsDirectoryPath,
        'AI_FLUTTER_AGENT_FLUTTER_EXE': config.flutterExecutable,
        'AI_FLUTTER_AGENT_DART_EXE': config.dartExecutable,
        'AI_FLUTTER_AGENT_TOOL_TIMEOUT_MS': config.toolTimeoutMs.toString(),
        'AI_FLUTTER_AGENT_TOOL_ID': definition['toolId']!.toString(),
      },
    ).timeout(Duration(milliseconds: config.toolTimeoutMs));

    final stdoutText = payload.stdout?.toString() ?? '';
    final stderrText = payload.stderr?.toString() ?? '';
    JsonMap response;
    try {
      response = ensureJsonMap(stdoutText.trim().isEmpty ? <String, dynamic>{} : jsonDecode(stdoutText));
    } catch (error) {
      throw StateError(
        'Generated Flutter tool ${definition['toolId']} returned non-JSON output. '
        'exitCode=${payload.exitCode}. stdout=$stdoutText stderr=$stderrText error=$error',
      );
    }

    if (payload.exitCode != 0 || response['ok'] != true) {
      final errorMessage = response['error']?.toString().trim().isNotEmpty == true
          ? response['error'].toString()
          : _combineProcessOutput(stdoutText, stderrText).trim();
      throw StateError(
        'Generated Flutter tool ${definition['toolId']} failed. '
        'exitCode=${payload.exitCode}. $errorMessage',
      );
    }
    return response['result'];
  }

  Future<JsonMap> _validateRunner(File runnerFile) async {
    final payload = await Process.run(
      config.dartExecutable,
      <String>[runnerFile.path],
      workingDirectory: config.projectRoot,
      runInShell: true,
      environment: <String, String>{
        'AI_FLUTTER_AGENT_VALIDATE_ONLY': '1',
        'AI_FLUTTER_AGENT_ARGS': base64Encode(utf8.encode('{}')),
        'AI_FLUTTER_AGENT_PROJECT_ROOT': config.projectRoot,
        'AI_FLUTTER_AGENT_STATE_DIR': config.stateDirectoryPath,
        'AI_FLUTTER_AGENT_GENERATED_TOOLS_DIR': config.generatedToolsDirectoryPath,
        'AI_FLUTTER_AGENT_FLUTTER_EXE': config.flutterExecutable,
        'AI_FLUTTER_AGENT_DART_EXE': config.dartExecutable,
        'AI_FLUTTER_AGENT_TOOL_TIMEOUT_MS': config.toolTimeoutMs.toString(),
      },
    ).timeout(Duration(milliseconds: config.toolTimeoutMs));

    if (payload.exitCode != 0) {
      throw StateError(
        'Generated Flutter tool validation failed. '
        'exitCode=${payload.exitCode}. ${_combineProcessOutput(payload.stdout?.toString() ?? '', payload.stderr?.toString() ?? '')}',
      );
    }

    final stdoutText = payload.stdout?.toString() ?? '';
    final parsed = stdoutText.trim().isEmpty ? <String, dynamic>{'validated': true} : ensureJsonMap(jsonDecode(stdoutText));
    return <String, dynamic>{
      'validated': parsed['ok'] == true || parsed['validated'] == true,
      'runnerPath': runnerFile.path,
    };
  }

  Future<File> _writeRunnerFile({
    required String fileName,
    required JsonMap definition,
  }) async {
    final runnerFile = _runnerFileFor(fileName);
    await runnerFile.parent.create(recursive: true);
    await runnerFile.writeAsString(_buildRunnerSource(definition));
    return runnerFile;
  }

  File _runnerFileFor(String fileName) {
    final base = _basename(fileName).replaceAll(RegExp(r'\.json$', caseSensitive: false), '');
    final safe = base.replaceAll(RegExp(r'[^A-Za-z0-9._-]+'), '_');
    return File(_join(config.generatedToolRuntimeDirectoryPath, '$safe.runner.dart'));
  }

  Future<List<File>> _listDefinitionFiles() async {
    final files = <File>[];
    await for (final entity in definitionsDirectory.list(recursive: false, followLinks: false)) {
      if (entity is File && entity.path.toLowerCase().endsWith('.json')) {
        files.add(entity);
      }
    }
    files.sort((left, right) => left.path.compareTo(right.path));
    return files;
  }

  String _buildRunnerSource(JsonMap definition) {
    final source = definition['source']!.toString();
    final buffer = StringBuffer()
      ..writeln("import 'dart:convert';")
      ..writeln("import 'dart:io';")
      ..writeln()
      ..writeln('typedef JsonMap = Map<String, dynamic>;')
      ..writeln()
      ..writeln('Future<void> main() async {')
      ..writeln("  if (Platform.environment['AI_FLUTTER_AGENT_VALIDATE_ONLY'] == '1') {")
      ..writeln("    stdout.write(jsonEncode({'ok': true, 'validated': true}));")
      ..writeln('    return;')
      ..writeln('  }')
      ..writeln('  try {')
      ..writeln("    final args = _decodeJsonMap(Platform.environment['AI_FLUTTER_AGENT_ARGS'] ?? 'e30=');")
      ..writeln('    final host = GeneratedToolHost.fromEnvironment(Platform.environment);')
      ..writeln('    final result = await runGeneratedTool(args, host);')
      ..writeln("    stdout.write(jsonEncode({'ok': true, 'result': result}));")
      ..writeln('  } catch (error, stackTrace) {')
      ..writeln("    stderr.writeln('\$error');")
      ..writeln("    stderr.writeln('\$stackTrace');")
      ..writeln("    stdout.write(jsonEncode({'ok': false, 'error': error.toString(), 'stack': stackTrace.toString()}));")
      ..writeln('    exitCode = 1;')
      ..writeln('  }')
      ..writeln('}')
      ..writeln()
      ..writeln('Future<Object?> runGeneratedTool(JsonMap args, GeneratedToolHost host) async {');
    buffer.writeln(source);
    buffer
      ..writeln('}')
      ..writeln()
      ..writeln('class GeneratedToolHost {')
      ..writeln('  GeneratedToolHost({')
      ..writeln('    required this.projectRoot,')
      ..writeln('    required this.stateDirectoryPath,')
      ..writeln('    required this.generatedToolsDirectoryPath,')
      ..writeln('    required this.flutterExecutable,')
      ..writeln('    required this.dartExecutable,')
      ..writeln('    required this.toolTimeoutMs,')
      ..writeln('  });')
      ..writeln()
      ..writeln('  factory GeneratedToolHost.fromEnvironment(Map<String, String> environment) {')
      ..writeln('    return GeneratedToolHost(')
      ..writeln("      projectRoot: environment['AI_FLUTTER_AGENT_PROJECT_ROOT'] ?? Directory.current.absolute.path,")
      ..writeln("      stateDirectoryPath: environment['AI_FLUTTER_AGENT_STATE_DIR'] ?? Directory.current.absolute.path,")
      ..writeln("      generatedToolsDirectoryPath: environment['AI_FLUTTER_AGENT_GENERATED_TOOLS_DIR'] ?? Directory.current.absolute.path,")
      ..writeln("      flutterExecutable: environment['AI_FLUTTER_AGENT_FLUTTER_EXE'] ?? 'flutter',")
      ..writeln("      dartExecutable: environment['AI_FLUTTER_AGENT_DART_EXE'] ?? 'dart',")
      ..writeln("      toolTimeoutMs: int.tryParse(environment['AI_FLUTTER_AGENT_TOOL_TIMEOUT_MS'] ?? '') ?? 120000,")
      ..writeln('    );')
      ..writeln('  }')
      ..writeln()
      ..writeln('  final String projectRoot;')
      ..writeln('  final String stateDirectoryPath;')
      ..writeln('  final String generatedToolsDirectoryPath;')
      ..writeln('  final String flutterExecutable;')
      ..writeln('  final String dartExecutable;')
      ..writeln('  final int toolTimeoutMs;')
      ..writeln()
      ..writeln('  Future<JsonMap> projectInfo() async {')
      ..writeln("    final pubspecFile = File(resolveProjectPath('pubspec.yaml'));")
      ..writeln('    final hasPubspec = await pubspecFile.exists();')
      ..writeln("    final pubspec = hasPubspec ? await pubspecFile.readAsString() : '';")
      ..writeln('    return <String, dynamic>{')
      ..writeln("      'projectRoot': _normalizePath(projectRoot),")
      ..writeln("      'hasPubspec': hasPubspec,")
      ..writeln("      'pubspecPath': hasPubspec ? 'pubspec.yaml' : '',")
      ..writeln("      'projectName': _yamlScalar(pubspec, 'name'),")
      ..writeln("      'description': _yamlScalar(pubspec, 'description'),")
      ..writeln("      'sdkConstraint': _yamlNestedScalar(pubspec, 'environment', 'sdk'),")
      ..writeln("      'hasLibDirectory': await Directory(resolveProjectPath('lib')).exists(),")
      ..writeln("      'hasTestDirectory': await Directory(resolveProjectPath('test')).exists(),")
      ..writeln("      'hasAndroidDirectory': await Directory(resolveProjectPath('android')).exists(),")
      ..writeln("      'hasIosDirectory': await Directory(resolveProjectPath('ios')).exists(),")
      ..writeln('    };')
      ..writeln('  }')
      ..writeln()
      ..writeln('  String resolveProjectPath(String relativePath) {')
      ..writeln("    final sanitizedRoot = _normalizePath(projectRoot);")
      ..writeln("    final rawPath = relativePath.replaceAll('\\\\', '/').trim();")
      ..writeln('    if (rawPath.isEmpty) {')
      ..writeln('      return sanitizedRoot;')
      ..writeln('    }')
      ..writeln("    final candidate = rawPath.startsWith('/')")
      ..writeln('        ? _normalizePath(rawPath)')
      ..writeln('        : _normalizePath(_joinPaths(sanitizedRoot, rawPath));')
      ..writeln("    if (candidate != sanitizedRoot && !candidate.startsWith('\$sanitizedRoot/')) {")
      ..writeln("      throw StateError('Path escapes the Flutter project root: \$relativePath');")
      ..writeln('    }')
      ..writeln('    return candidate;')
      ..writeln('  }')
      ..writeln()
      ..writeln('  String relativeToProject(String absolutePath) {')
      ..writeln("    final root = _normalizePath(projectRoot);")
      ..writeln('    final candidate = _normalizePath(absolutePath);')
      ..writeln('    if (candidate == root) {')
      ..writeln("      return '.';")
      ..writeln('    }')
      ..writeln("    if (candidate.startsWith('\$root/')) {")
      ..writeln('      return candidate.substring(root.length + 1);')
      ..writeln('    }')
      ..writeln("    throw StateError('Path is outside the Flutter project root: \$absolutePath');")
      ..writeln('  }')
      ..writeln()
      ..writeln('  Future<String> readText(String path) async {')
      ..writeln('    final file = File(resolveProjectPath(path));')
      ..writeln('    if (!await file.exists()) {')
      ..writeln("      throw StateError('File not found: \$path');")
      ..writeln('    }')
      ..writeln('    return file.readAsString();')
      ..writeln('  }')
      ..writeln()
      ..writeln('  Future<Object?> readJson(String path) async {')
      ..writeln('    return jsonDecode(await readText(path));')
      ..writeln('  }')
      ..writeln()
      ..writeln('  Future<JsonMap> writeText(String path, String content, {bool createDirectories = true}) async {')
      ..writeln('    final file = File(resolveProjectPath(path));')
      ..writeln('    if (createDirectories) {')
      ..writeln('      await file.parent.create(recursive: true);')
      ..writeln('    }')
      ..writeln('    await file.writeAsString(content);')
      ..writeln('    return <String, dynamic>{')
      ..writeln("      'path': path,")
      ..writeln("      'sizeBytes': utf8.encode(content).length,")
      ..writeln('    };')
      ..writeln('  }')
      ..writeln()
      ..writeln('  Future<JsonMap> writeJson(String path, Object? value, {bool createDirectories = true, bool pretty = true}) async {')
      ..writeln("    final text = pretty ? const JsonEncoder.withIndent('  ').convert(value) : jsonEncode(value);")
      ..writeln('    return writeText(path, text, createDirectories: createDirectories);')
      ..writeln('  }')
      ..writeln()
      ..writeln('  Future<JsonMap> deleteFile(String path) async {')
      ..writeln('    final file = File(resolveProjectPath(path));')
      ..writeln('    final existed = await file.exists();')
      ..writeln('    if (existed) {')
      ..writeln('      await file.delete();')
      ..writeln('    }')
      ..writeln("    return <String, dynamic>{'path': path, 'deleted': existed};")
      ..writeln('  }')
      ..writeln()
      ..writeln('  Future<bool> exists(String path) async {')
      ..writeln('    return FileSystemEntity.typeSync(resolveProjectPath(path)) != FileSystemEntityType.notFound;')
      ..writeln('  }')
      ..writeln()
      ..writeln('  Future<JsonMap> stat(String path) async {')
      ..writeln('    final entityPath = resolveProjectPath(path);')
      ..writeln('    final type = FileSystemEntity.typeSync(entityPath);')
      ..writeln('    if (type == FileSystemEntityType.notFound) {')
      ..writeln("      throw StateError('Path not found: \$path');")
      ..writeln('    }')
      ..writeln('    final stat = await FileStat.stat(entityPath);')
      ..writeln('    return <String, dynamic>{')
      ..writeln("      'path': path,")
      ..writeln("      'type': type.name,")
      ..writeln("      'sizeBytes': stat.size,")
      ..writeln("      'modifiedAt': stat.modified.toUtc().toIso8601String(),")
      ..writeln('    };')
      ..writeln('  }')
      ..writeln()
      ..writeln('  Future<List<JsonMap>> listFiles({String under = ".", List<String>? extensions, String contains = "", int maxEntries = 1000}) async {')
      ..writeln('    final directory = Directory(resolveProjectPath(under == "." ? "" : under));')
      ..writeln('    if (!await directory.exists()) {')
      ..writeln("      throw StateError('Directory not found: \$under');")
      ..writeln('    }')
      ..writeln('    final normalizedExtensions = (extensions ?? <String>[]).map((item) => item.trim().toLowerCase()).where((item) => item.isNotEmpty).toSet();')
      ..writeln('    final lowerContains = contains.toLowerCase();')
      ..writeln('    final items = <JsonMap>[];')
      ..writeln('    await for (final entity in directory.list(recursive: true, followLinks: false)) {')
      ..writeln('      if (entity is! File) {')
      ..writeln('        continue;')
      ..writeln('      }')
      ..writeln('      final relativePath = relativeToProject(entity.path);')
      ..writeln('      if (normalizedExtensions.isNotEmpty && !normalizedExtensions.any((ext) => relativePath.toLowerCase().endsWith(ext))) {')
      ..writeln('        continue;')
      ..writeln('      }')
      ..writeln('      if (lowerContains.isNotEmpty && !relativePath.toLowerCase().contains(lowerContains)) {')
      ..writeln('        continue;')
      ..writeln('      }')
      ..writeln('      final stat = await entity.stat();')
      ..writeln('      items.add(<String, dynamic>{')
      ..writeln("        'path': relativePath,")
      ..writeln("        'sizeBytes': stat.size,")
      ..writeln("        'modifiedAt': stat.modified.toUtc().toIso8601String(),")
      ..writeln('      });')
      ..writeln('      if (items.length >= maxEntries) {')
      ..writeln('        break;')
      ..writeln('      }')
      ..writeln('    }')
      ..writeln('    return items;')
      ..writeln('  }')
      ..writeln()
      ..writeln('  Future<List<JsonMap>> searchText({required String query, String under = "lib", List<String>? extensions, int maxMatches = 100}) async {')
      ..writeln('    final directory = Directory(resolveProjectPath(under));')
      ..writeln('    if (!await directory.exists()) {')
      ..writeln("      throw StateError('Directory not found: \$under');")
      ..writeln('    }')
      ..writeln("    final extensionSet = (extensions == null || extensions.isEmpty)")
      ..writeln("        ? <String>{'.dart', '.yaml', '.yml', '.json', '.md', '.txt'}")
      ..writeln('        : extensions.map((item) => item.trim().toLowerCase()).where((item) => item.isNotEmpty).toSet();')
      ..writeln('    final matches = <JsonMap>[];')
      ..writeln('    await for (final entity in directory.list(recursive: true, followLinks: false)) {')
      ..writeln("      if (entity is! File || !extensionSet.any((ext) => entity.path.toLowerCase().endsWith(ext))) {")
      ..writeln('        continue;')
      ..writeln('      }')
      ..writeln('      String content;')
      ..writeln('      try {')
      ..writeln('        content = await entity.readAsString();')
      ..writeln('      } on FileSystemException {')
      ..writeln('        continue;')
      ..writeln('      }')
      ..writeln('      final relativePath = relativeToProject(entity.path);')
      ..writeln('      final lines = const LineSplitter().convert(content);')
      ..writeln('      for (var index = 0; index < lines.length; index++) {')
      ..writeln('        final line = lines[index];')
      ..writeln('        final column = line.toLowerCase().indexOf(query.toLowerCase());')
      ..writeln('        if (column < 0) {')
      ..writeln('          continue;')
      ..writeln('        }')
      ..writeln('        matches.add(<String, dynamic>{')
      ..writeln("          'path': relativePath,")
      ..writeln("          'line': index + 1,")
      ..writeln("          'column': column + 1,")
      ..writeln("          'snippet': line.trim(),")
      ..writeln('        });')
      ..writeln('        if (matches.length >= maxMatches) {')
      ..writeln('          break;')
      ..writeln('        }')
      ..writeln('      }')
      ..writeln('      if (matches.length >= maxMatches) {')
      ..writeln('        break;')
      ..writeln('      }')
      ..writeln('    }')
      ..writeln('    return matches;')
      ..writeln('  }')
      ..writeln()
      ..writeln('  Future<JsonMap> runCommand(String executable, List<String> arguments, {String? workingDirectory, bool runInShell = true, Map<String, String>? environment}) async {')
      ..writeln("    final resolvedWorkingDirectory = (workingDirectory == null || workingDirectory.trim().isEmpty)")
      ..writeln('        ? projectRoot')
      ..writeln('        : (workingDirectory.trim().startsWith("/")')
      ..writeln('            ? _normalizePath(workingDirectory.trim())')
      ..writeln('            : resolveProjectPath(workingDirectory.trim()));')
      ..writeln('    final result = await Process.run(')
      ..writeln('      executable,')
      ..writeln('      arguments,')
      ..writeln('      workingDirectory: resolvedWorkingDirectory,')
      ..writeln('      runInShell: runInShell,')
      ..writeln('      environment: environment,')
      ..writeln('    ).timeout(Duration(milliseconds: toolTimeoutMs));')
      ..writeln('    final stdoutText = result.stdout?.toString() ?? "";')
      ..writeln('    final stderrText = result.stderr?.toString() ?? "";')
      ..writeln('    return <String, dynamic>{')
      ..writeln("      'command': <String>[executable, ...arguments],")
      ..writeln("      'workingDirectory': resolvedWorkingDirectory,")
      ..writeln("      'exitCode': result.exitCode,")
      ..writeln("      'stdout': stdoutText,")
      ..writeln("      'stderr': stderrText,")
      ..writeln("      'output': _combineOutput(stdoutText, stderrText),")
      ..writeln('    };')
      ..writeln('  }')
      ..writeln()
      ..writeln('  Future<JsonMap> runFlutter(List<String> arguments, {String? workingDirectory}) {')
      ..writeln('    return runCommand(flutterExecutable, arguments, workingDirectory: workingDirectory);')
      ..writeln('  }')
      ..writeln()
      ..writeln('  Future<JsonMap> runDart(List<String> arguments, {String? workingDirectory}) {')
      ..writeln('    return runCommand(dartExecutable, arguments, workingDirectory: workingDirectory);')
      ..writeln('  }')
      ..writeln('}')
      ..writeln()
      ..writeln('JsonMap _decodeJsonMap(String base64Value) {')
      ..writeln('  final decoded = jsonDecode(utf8.decode(base64Decode(base64Value)));')
      ..writeln('  if (decoded is Map<String, dynamic>) {')
      ..writeln('    return decoded;')
      ..writeln('  }')
      ..writeln('  if (decoded is Map) {')
      ..writeln('    return decoded.map((key, value) => MapEntry(key.toString(), value));')
      ..writeln('  }')
      ..writeln("  throw const FormatException('Expected a JSON object.');")
      ..writeln('}')
      ..writeln()
      ..writeln('String _normalizePath(String path) => File(path).absolute.path.replaceAll("\\\\", "/");')
      ..writeln()
      ..writeln('String _joinPaths(String left, String right) {')
      ..writeln("  final normalizedLeft = left.replaceAll('\\\\', '/').replaceAll(RegExp(r'/+\$'), '');")
      ..writeln("  final normalizedRight = right.replaceAll('\\\\', '/').replaceAll(RegExp(r'^/+'), '');")
      ..writeln("  return '\$normalizedLeft/\$normalizedRight';")
      ..writeln('}')
      ..writeln()
      ..writeln('String _yamlScalar(String yaml, String key) {')
      ..writeln("  final regex = RegExp('^\\\\s*' + RegExp.escape(key) + r'\\\\s*:\\\\s*(.+?)\\\\s*\$', multiLine: true);")
      ..writeln('  final match = regex.firstMatch(yaml);')
      ..writeln("  return match == null ? '' : match.group(1)?.trim() ?? '';")
      ..writeln('}')
      ..writeln()
      ..writeln('String _yamlNestedScalar(String yaml, String section, String key) {')
      ..writeln("  final sectionRegex = RegExp('^\\\\s*' + RegExp.escape(section) + r'\\\\s*:\\\\s*(?:\\\\r?\\\\n)([\\\\s\\\\S]*?)(?=^\\\\S|\\\\Z)', multiLine: true);")
      ..writeln('  final sectionMatch = sectionRegex.firstMatch(yaml);')
      ..writeln('  if (sectionMatch == null) {')
      ..writeln("    return '';")
      ..writeln('  }')
      ..writeln("  final nestedRegex = RegExp(r'^\\s{2,}' + RegExp.escape(key) + r'\\s*:\\s*(.+?)\\s*\$', multiLine: true);")
      ..writeln('  final nestedMatch = nestedRegex.firstMatch(sectionMatch.group(1) ?? "");')
      ..writeln("  return nestedMatch == null ? '' : nestedMatch.group(1)?.trim() ?? '';")
      ..writeln('}')
      ..writeln()
      ..writeln('String _combineOutput(String stdoutText, String stderrText) {')
      ..writeln('  if (stdoutText.isEmpty && stderrText.isEmpty) {')
      ..writeln("    return '';")
      ..writeln('  }')
      ..writeln('  if (stderrText.isEmpty) {')
      ..writeln('    return stdoutText;')
      ..writeln('  }')
      ..writeln('  if (stdoutText.isEmpty) {')
      ..writeln('    return stderrText;')
      ..writeln('  }')
      ..writeln("  return 'STDOUT:\\n\$stdoutText\\n\\nSTDERR:\\n\$stderrText';")
      ..writeln('}');
    return buffer.toString();
  }

  JsonMap _normalizeDefinition(Object? rawDefinition) {
    final definition = ensureJsonMap(rawDefinition);
    final toolId = definition['toolId']?.toString().trim() ?? '';
    final description = definition['description']?.toString().trim() ?? '';
    final source = definition['source']?.toString() ?? '';
    if (toolId.isEmpty) {
      throw StateError('definition.toolId is required.');
    }
    if (description.isEmpty) {
      throw StateError('definition.description is required.');
    }
    if (source.trim().isEmpty) {
      throw StateError('definition.source is required.');
    }
    return <String, dynamic>{
      'toolId': toolId,
      'description': description,
      'danger': _normalizeDanger(definition['danger']?.toString() ?? 'low'),
      'requiresConfirmation': definition['requiresConfirmation'] == true,
      'argsSchema': _ensureSchemaObject(definition['argsSchema']),
      'returnSchema': _ensureSchemaObject(definition['returnSchema']),
      'source': source,
    };
  }

  Map<String, dynamic> _ensureSchemaObject(Object? value) {
    final schema = ensureJsonMap(value);
    return schema.isEmpty ? <String, dynamic>{'type': 'object'} : schema;
  }

  AiToolDanger _dangerFromString(String value) {
    switch (value.trim().toLowerCase()) {
      case 'high':
        return AiToolDanger.high;
      case 'medium':
        return AiToolDanger.medium;
      default:
        return AiToolDanger.low;
    }
  }

  String _normalizeDanger(String value) {
    switch (value.trim().toLowerCase()) {
      case 'high':
        return 'high';
      case 'medium':
        return 'medium';
      default:
        return 'low';
    }
  }

  String _normalizeFileName(String fileName) {
    final trimmed = fileName.trim();
    if (trimmed.isEmpty) {
      throw StateError('fileName is required.');
    }
    final safeName = _basename(trimmed);
    if (safeName != trimmed) {
      throw StateError('Subdirectories are not allowed. Provide only a file name.');
    }
    if (!safeName.toLowerCase().endsWith('.json')) {
      throw StateError('Generated tool files must use the .json extension.');
    }
    return safeName;
  }

  String _suggestedFileName(String toolId) {
    final buffer = StringBuffer();
    for (final rune in toolId.runes) {
      final character = String.fromCharCode(rune);
      buffer.write(RegExp(r'[A-Za-z0-9._-]').hasMatch(character) ? character : '_');
    }
    return '${buffer.toString()}.json';
  }
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

String _basename(String path) => path.replaceAll('\\', '/').split('/').last;

String _join(String left, String right) {
  final normalizedLeft = left.replaceAll('\\', '/').replaceAll(RegExp(r'/+$'), '');
  final normalizedRight = right.replaceAll('\\', '/').replaceAll(RegExp(r'^/+'), '');
  return '$normalizedLeft/$normalizedRight';
}
