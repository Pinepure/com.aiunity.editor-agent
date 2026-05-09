import 'dart:async';
import 'dart:io';

import 'package:ai_flutter_agent/ai_flutter_agent.dart';

Future<void> main(List<String> args) async {
  if (args.contains('--help') || args.contains('-h')) {
    stdout.writeln(_usage);
    return;
  }

  final options = _parseArgs(args);
  final projectRoot = Directory(options['project-root'] ?? Directory.current.path).absolute.path;
  final host = options['host'] ?? '127.0.0.1';
  final port = int.tryParse(options['port'] ?? '19777') ?? 19777;
  final toolTimeoutMs = int.tryParse(options['tool-timeout-ms'] ?? '120000') ?? 120000;
  final requireToken = !options.containsKey('no-token');
  final fullAccessEnabled = options.containsKey('full-access');
  final flutterExecutable = options['flutter-executable'] ?? 'flutter';

  final config = AiFlutterAgentConfig(
    projectRoot: projectRoot,
    host: host,
    port: port,
    requireToken: requireToken,
    fullAccessEnabled: fullAccessEnabled,
    flutterExecutable: flutterExecutable,
    toolTimeoutMs: toolTimeoutMs,
  );

  final server = AiFlutterAgentServer(config);
  await server.start();

  stdout.writeln('AI Flutter Agent listening at ${config.serverUrl}');
  if (requireToken) {
    stdout.writeln('Token file: ${config.tokenFilePath}');
  } else {
    stdout.writeln('Token auth disabled.');
  }

  Future<void> shutdown() async {
    await server.stop();
    exit(0);
  }

  ProcessSignal.sigint.watch().listen((_) {
    unawaited(shutdown());
  });
  ProcessSignal.sigterm.watch().listen((_) {
    unawaited(shutdown());
  });
}

Map<String, String> _parseArgs(List<String> args) {
  final values = <String, String>{};
  for (var index = 0; index < args.length; index++) {
    final current = args[index];
    if (!current.startsWith('--')) {
      continue;
    }

    final keyValue = current.substring(2).split('=');
    if (keyValue.length > 1) {
      values[keyValue.first] = keyValue.sublist(1).join('=');
      continue;
    }

    final key = keyValue.first;
    final nextIndex = index + 1;
    if (nextIndex < args.length && !args[nextIndex].startsWith('--')) {
      values[key] = args[nextIndex];
      index = nextIndex;
      continue;
    }

    values[key] = 'true';
  }

  return values;
}

const String _usage = '''
AI Flutter Agent

Usage:
  dart run bin/ai_flutter_agent.dart [options]

Options:
  --project-root <path>         Flutter project root. Defaults to the current directory.
  --host <host>                 Bind host. Defaults to 127.0.0.1.
  --port <port>                 Bind port. Defaults to 19777.
  --no-token                    Disable token auth for protected endpoints.
  --full-access                 Allow high-risk mutation tools.
  --flutter-executable <path>   Flutter CLI executable. Defaults to flutter.
  --tool-timeout-ms <ms>        Max tool run time in milliseconds. Defaults to 120000.
  --help                        Show this help.
''';
