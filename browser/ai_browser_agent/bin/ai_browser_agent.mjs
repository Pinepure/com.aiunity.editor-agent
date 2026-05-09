#!/usr/bin/env node

import process from 'node:process';

import { AiBrowserAgentConfig } from '../lib/models.mjs';
import { AiBrowserAgentServer } from '../lib/agent_server.mjs';

function printUsage() {
  console.log(`AI Browser Agent

Usage:
  ai-browser-agent [options]

Options:
  --root-dir <path>           Workspace root for token and state files. Default: current directory
  --host <host>               HTTP bind host. Default: 127.0.0.1
  --port <port>               HTTP bind port. Default: 19778
  --no-token                  Disable token authentication
  --full-access               Enable high-risk tools such as token regeneration and screenshot writes
  --chrome-host <host>        Chrome remote debugging host. Default: 127.0.0.1
  --chrome-port <port>        Chrome remote debugging port. Default: 9222
  --chrome-executable <path>  Chrome executable to launch when browser.chrome_launch is used
  --tool-timeout-ms <ms>      Per-tool timeout. Default: 120000
  --help                      Show this help
`);
}

function parseArgs(argv) {
  const options = {
    rootDir: process.cwd(),
    host: '127.0.0.1',
    port: 19778,
    requireToken: true,
    fullAccessEnabled: false,
    chromeHost: '127.0.0.1',
    chromePort: 9222,
    chromeExecutable: '',
    toolTimeoutMs: 120000,
  };

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const nextValue = () => {
      index += 1;
      if (index >= argv.length) {
        throw new Error(`Missing value for ${arg}`);
      }
      return argv[index];
    };
    switch (arg) {
      case '--root-dir':
        options.rootDir = nextValue();
        break;
      case '--host':
        options.host = nextValue();
        break;
      case '--port':
        options.port = Number(nextValue());
        break;
      case '--no-token':
        options.requireToken = false;
        break;
      case '--full-access':
        options.fullAccessEnabled = true;
        break;
      case '--chrome-host':
        options.chromeHost = nextValue();
        break;
      case '--chrome-port':
        options.chromePort = Number(nextValue());
        break;
      case '--chrome-executable':
        options.chromeExecutable = nextValue();
        break;
      case '--tool-timeout-ms':
        options.toolTimeoutMs = Number(nextValue());
        break;
      case '--help':
      case '-h':
        printUsage();
        process.exit(0);
        break;
      default:
        throw new Error(`Unknown argument: ${arg}`);
    }
  }

  return options;
}

async function main() {
  const config = new AiBrowserAgentConfig(parseArgs(process.argv.slice(2)));
  const server = new AiBrowserAgentServer(config);
  await server.start();
  console.log(`AI Browser Agent listening on ${config.serverUrl}`);
  if (config.requireToken) {
    console.log(`Token file: ${config.tokenFilePath}`);
  }

  const shutdown = async () => {
    try {
      await server.stop();
    } finally {
      process.exit(0);
    }
  };

  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
}

main().catch((error) => {
  console.error(String(error));
  process.exit(1);
});
