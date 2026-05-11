const path = require('node:path');
const fs = require('node:fs/promises');

const { AiToolDefinition, clampNumber, schemaJson } = require('./models');

class VsCodeGeneratedToolHost {
  constructor({ config, vscodeApi, runtimeState, resultHandleStore }) {
    this.config = config;
    this.vscodeApi = vscodeApi;
    this.runtimeState = runtimeState;
    this.resultHandleStore = resultHandleStore;
  }

  get definitionsDir() {
    return this.config.generatedToolsDirectoryPath;
  }

  async ensureLayout() {
    await fs.mkdir(this.definitionsDir, { recursive: true });
  }

  async computeFingerprint() {
    await this.ensureLayout();
    const files = await this.listFiles();
    const parts = [];
    for (const fileName of files) {
      const filePath = path.join(this.definitionsDir, fileName);
      const content = await fs.readFile(filePath, 'utf8');
      parts.push(`${fileName}\n${content}`);
    }
    return parts.join('\n---\n');
  }

  async listDefinitions() {
    await this.ensureLayout();
    const items = [];
    for (const fileName of await this.listFiles()) {
      const definition = await this.readDefinition(fileName);
      items.push({
        fileName,
        toolId: definition.toolId,
        description: definition.description,
        danger: definition.danger || 'low',
        requiresConfirmation: Boolean(definition.requiresConfirmation),
      });
    }
    return items;
  }

  async loadDefinitions() {
    await this.ensureLayout();
    const definitions = [];
    for (const fileName of await this.listFiles()) {
      const definition = await this.readDefinition(fileName);
      definitions.push({ ...definition, fileName });
    }
    return definitions;
  }

  async upsertDefinition({ fileName, definition }) {
    await this.ensureLayout();
    const safeName = normalizeGeneratedToolFileName(fileName || suggestedFileName(definition.toolId || 'generated.example'));
    const payload = normalizeGeneratedToolDefinition(definition);
    await fs.writeFile(path.join(this.definitionsDir, safeName), `${JSON.stringify(payload, null, 2)}\n`, 'utf8');
    return { fileName: safeName, path: path.join(this.definitionsDir, safeName) };
  }

  async deleteDefinition(fileName) {
    await this.ensureLayout();
    const safeName = normalizeGeneratedToolFileName(fileName);
    try {
      await fs.unlink(path.join(this.definitionsDir, safeName));
      return { deleted: true, fileName: safeName };
    } catch (error) {
      if (error && error.code === 'ENOENT') {
        return { deleted: false, fileName: safeName };
      }
      throw error;
    }
  }

  getTemplate({ toolId = 'generated.vscode_example', description = 'Generated VS Code tool.', fileName = '' } = {}) {
    return {
      fileName: normalizeGeneratedToolFileName(fileName || suggestedFileName(toolId)),
      definition: {
        toolId,
        description,
        danger: 'low',
        requiresConfirmation: false,
        argsSchema: {
          type: 'object',
          additionalProperties: false,
          properties: {
            name: { type: 'string' },
          },
        },
        returnSchema: {
          type: 'object',
          additionalProperties: true,
        },
        source:
`const name = String(args.name || 'VS Code');
const summary = await host.workspaceSummary();
return {
  message: \`Hello, \${name}\`,
  workspaceFolders: summary.workspaceFolders.length,
  activeEditor: summary.activeEditor,
};`,
      },
      notes: [
        'The source body runs as an async JavaScript function with arguments (args, host, require, console).',
        'Use host.vscode for the VS Code API, host.fs for Node fs/promises, and host.path for path helpers.',
        'Return plain JSON-serializable objects, arrays, strings, booleans, or numbers.',
      ],
    };
  }

  createToolDefinition(definition, context) {
    return new AiToolDefinition({
      id: definition.toolId,
      description: definition.description,
      argsSchemaJson: schemaJson(definition.argsSchema),
      returnSchemaJson: schemaJson(definition.returnSchema),
      handlerName: `generated:${definition.fileName}`,
      danger: definition.danger || 'low',
      requiresConfirmation: Boolean(definition.requiresConfirmation),
      handler: async (args) => this.executeDefinition(definition, args, context),
    });
  }

  async executeDefinition(definition, args, context) {
    const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
    const executionHost = this.createExecutionHost(context, definition);
    const runner = new AsyncFunction('args', 'host', 'require', 'console', definition.source);
    return runner(args, executionHost, require, console);
  }

  createExecutionHost(context, definition) {
    const host = this;
    return {
      vscode: context.vscodeApi,
      fs,
      path,
      workspaceRoot: context.config.rootDir,
      workspaceFolders: context.vscodeApi.workspace.workspaceFolders
        ? context.vscodeApi.workspace.workspaceFolders.map((folder) => ({
          name: folder.name,
          path: folder.uri.fsPath,
          uri: folder.uri.toString(),
        }))
        : [],
      workspaceSummary: async () => ({
        workspaceFolders: context.vscodeApi.workspace.workspaceFolders
          ? context.vscodeApi.workspace.workspaceFolders.map((folder) => ({
            name: folder.name,
            path: folder.uri.fsPath,
            uri: folder.uri.toString(),
          }))
          : [],
        activeEditor: context.vscodeApi.window.activeTextEditor
          ? {
            path: context.vscodeApi.window.activeTextEditor.document.uri.fsPath,
            uri: context.vscodeApi.window.activeTextEditor.document.uri.toString(),
            languageId: context.vscodeApi.window.activeTextEditor.document.languageId,
            isDirty: context.vscodeApi.window.activeTextEditor.document.isDirty,
          }
          : null,
      }),
      readText: async (target) => {
        const uri = resolveHostUri(context, target);
        return Buffer.from(await context.vscodeApi.workspace.fs.readFile(uri)).toString('utf8');
      },
      writeText: async (target, text) => {
        const uri = resolveHostUri(context, target);
        await context.vscodeApi.workspace.fs.writeFile(uri, Buffer.from(String(text), 'utf8'));
        return { uri: uri.toString(), path: uri.fsPath };
      },
      findFiles: async (include = '**/*', exclude = '**/{node_modules,.git,Library,Temp}/**', maxResults = 1000) => {
        const uris = await context.vscodeApi.workspace.findFiles(
          String(include || '**/*'),
          String(exclude || '**/{node_modules,.git,Library,Temp}/**'),
          clampNumber(Number(maxResults) || 1000, 1, 20000),
        );
        return uris.map((uri) => ({
          uri: uri.toString(),
          path: uri.fsPath,
          relativePath: relativeWorkspacePath(context, uri.fsPath),
        }));
      },
      executeCommand: async (commandId, ...commandArgs) => context.vscodeApi.commands.executeCommand(String(commandId), ...commandArgs),
      createItemsResult: ({ sourceToolId = definition.toolId, fieldName = 'items', items = [], summary = {}, pageSize = 100 } = {}) => host.resultHandleStore.buildItemsResult({
        sourceToolId,
        fieldName,
        items,
        summary,
        pageSize: clampNumber(Number(pageSize) || 100, 1, 200),
      }),
      createTextResult: ({ sourceToolId = 'generated.text', text = '', summary = {}, length = 4096 } = {}) => host.resultHandleStore.buildTextResult({
        sourceToolId,
        text: String(text),
        summary,
        length: clampNumber(Number(length) || 4096, 1, 32768),
      }),
    };
  }

  async readDefinition(fileName) {
    const safeName = normalizeGeneratedToolFileName(fileName);
    const raw = JSON.parse(await fs.readFile(path.join(this.definitionsDir, safeName), 'utf8'));
    return normalizeGeneratedToolDefinition(raw);
  }

  async listFiles() {
    const entries = await fs.readdir(this.definitionsDir, { withFileTypes: true });
    return entries
      .filter((entry) => entry.isFile() && entry.name.endsWith('.json'))
      .map((entry) => entry.name)
      .sort((left, right) => left.localeCompare(right));
  }
}

function normalizeGeneratedToolDefinition(definition) {
  const toolId = String(definition.toolId || '').trim();
  if (!toolId) {
    throw new Error('definition.toolId is required.');
  }
  const description = String(definition.description || '').trim();
  if (!description) {
    throw new Error('definition.description is required.');
  }
  const source = String(definition.source || '');
  if (!source.trim()) {
    throw new Error('definition.source is required.');
  }
  return {
    toolId,
    description,
    danger: String(definition.danger || 'low'),
    requiresConfirmation: Boolean(definition.requiresConfirmation),
    argsSchema: ensureSchemaObject(definition.argsSchema),
    returnSchema: ensureSchemaObject(definition.returnSchema),
    source,
  };
}

function ensureSchemaObject(value) {
  return value && typeof value === 'object' && !Array.isArray(value) ? value : { type: 'object' };
}

function normalizeGeneratedToolFileName(fileName) {
  const trimmed = String(fileName || '').trim();
  if (!trimmed) {
    throw new Error('fileName is required.');
  }
  const safeName = path.basename(trimmed);
  if (safeName !== trimmed) {
    throw new Error('Subdirectories are not allowed. Provide only a file name.');
  }
  if (!safeName.endsWith('.json')) {
    throw new Error('Generated tool files must use the .json extension.');
  }
  return safeName;
}

function suggestedFileName(toolId) {
  return `${String(toolId || 'generated.example').replace(/[^a-z0-9._-]+/gi, '_')}.json`;
}

function resolveHostUri(context, target) {
  if (target && typeof target === 'object' && typeof target.uri === 'string') {
    return context.vscodeApi.Uri.parse(target.uri);
  }
  const raw = String(target || '');
  if (raw.startsWith('file:') || raw.includes('://')) {
    return context.vscodeApi.Uri.parse(raw);
  }
  const resolved = path.isAbsolute(raw) ? raw : path.join(context.config.rootDir, raw);
  return context.vscodeApi.Uri.file(path.resolve(resolved));
}

function relativeWorkspacePath(context, absolutePath) {
  const folders = context.vscodeApi.workspace.workspaceFolders || [];
  for (const folder of folders) {
    if (absolutePath.startsWith(folder.uri.fsPath)) {
      return path.relative(folder.uri.fsPath, absolutePath);
    }
  }
  return absolutePath;
}

module.exports = {
  VsCodeGeneratedToolHost,
};
