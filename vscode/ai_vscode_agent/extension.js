const vscode = require('vscode');

const { AiVsCodeAgentConfig } = require('./lib/models');
const { AiVsCodeAgentServer } = require('./lib/agent_server');

let server = null;

async function activate(context) {
  const configuration = vscode.workspace.getConfiguration('aiPlatformAgent.vscode');
  if (!configuration.get('enabled', true)) {
    return;
  }

  const workspaceFolder = vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0
    ? vscode.workspace.workspaceFolders[0].uri.fsPath
    : context.globalStorageUri.fsPath;

  const config = new AiVsCodeAgentConfig({
    rootDir: workspaceFolder,
    storageDir: context.globalStorageUri.fsPath,
    extensionDir: context.extensionUri.fsPath,
    host: configuration.get('host', '127.0.0.1'),
    port: Number(configuration.get('port', 19790)),
    requireToken: Boolean(configuration.get('requireToken', true)),
    fullAccessEnabled: Boolean(configuration.get('fullAccess', false)),
    toolTimeoutMs: Number(configuration.get('toolTimeoutMs', 120000)),
  });

  server = new AiVsCodeAgentServer(config, context, vscode);
  await server.start();

  const generatedToolsWatcher = vscode.workspace.createFileSystemWatcher(
    new vscode.RelativePattern(
      vscode.Uri.file(config.rootDir),
      '.ai_platform_agent/vscode/generated_tools/*.json',
    ),
  );
  generatedToolsWatcher.onDidCreate(() => server && server.markGeneratedToolsDirty('file created'));
  generatedToolsWatcher.onDidChange(() => server && server.markGeneratedToolsDirty('file changed'));
  generatedToolsWatcher.onDidDelete(() => server && server.markGeneratedToolsDirty('file deleted'));

  context.subscriptions.push(
    generatedToolsWatcher,
    vscode.commands.registerCommand('aiPlatformAgent.vscode.showStatus', async () => {
      const payload = server.buildHealthPayload({ includeOk: true });
      await vscode.window.showInformationMessage(
        `AI VS Code Agent ${payload.serverRunning ? 'running' : 'stopped'} on ${payload.serverUrl} · token: ${payload.platform.tokenFilePath}`,
      );
    }),
    {
      dispose() {
        if (server) {
          server.stop().catch(() => {});
        }
      },
    },
  );
}

async function deactivate() {
  if (server) {
    await server.stop();
    server = null;
  }
}

module.exports = {
  activate,
  deactivate,
};
