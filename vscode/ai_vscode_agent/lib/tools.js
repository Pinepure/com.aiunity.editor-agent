const path = require('node:path');
const fs = require('node:fs/promises');

const {
  AiManifestBundleDefinition,
  AiToolDefinition,
  clampNumber,
  schemaJson,
} = require('./models');

function defaultBundles() {
  return [
    new AiManifestBundleDefinition({
      id: 'service',
      description: 'Adapter health, logs, config, and recent calls.',
      prefixes: ['service.'],
    }),
    new AiManifestBundleDefinition({
      id: 'vscode.workspace',
      description: 'Workspace, files, and document inspection tools.',
      prefixes: ['vscode.workspace_', 'vscode.files_', 'vscode.document_', 'vscode.search_'],
    }),
    new AiManifestBundleDefinition({
      id: 'vscode.ide',
      description: 'Diagnostics, symbols, commands, and task inspection tools.',
      prefixes: ['vscode.diagnostics_', 'vscode.symbols_', 'vscode.commands_', 'vscode.tasks_'],
    }),
    new AiManifestBundleDefinition({
      id: 'vscode.execute',
      description: 'Controlled execution tools inside VS Code.',
      prefixes: ['vscode.task_run'],
    }),
    new AiManifestBundleDefinition({
      id: 'tooling.dynamic',
      description: 'Generated tool management and dynamic registration tools.',
      prefixes: ['tool.'],
    }),
  ];
}

function registerDefaultTools(registry, context) {
  const register = (payload) => registry.register(new AiToolDefinition(payload));
  register({
    id: 'service.health_get',
    description: 'Return the adapter health payload.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: {} }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'service.health_get',
    handler: async (args, ctx) => ctx.buildHealthPayload({ includeOk: true }),
  });
  register({
    id: 'service.agent_brief_get',
    description: 'Return the concise operating brief for this adapter.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: {} }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'service.agent_brief_get',
    handler: async (args, ctx) => ctx.buildAgentBriefPayload({ includeOk: true }),
  });
  register({
    id: 'service.config_get',
    description: 'Return effective adapter configuration and workspace defaults.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: {} }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'service.config_get',
    handler: async (args, ctx) => ({
      rootDir: ctx.config.rootDir,
      storageDir: ctx.config.storageDir,
      extensionDir: ctx.config.extensionDir,
      serverUrl: ctx.config.serverUrl,
      tokenFilePath: ctx.config.tokenFilePath,
      requireToken: ctx.config.requireToken,
      fullAccessEnabled: ctx.config.fullAccessEnabled,
      acceptedTokenHeaders: ctx.config.acceptedTokenHeaders,
      supportsDynamicToolRegistration: true,
      generatedToolsDirectoryPath: ctx.config.generatedToolsDirectoryPath,
      workspaceFolders: ctx.vscodeApi.workspace.workspaceFolders
        ? ctx.vscodeApi.workspace.workspaceFolders.map((folder) => ({
          name: folder.name,
          path: folder.uri.fsPath,
        }))
        : [],
    }),
  });
  register({
    id: 'tool.get_template',
    description: 'Return a safe template for a generated VS Code tool definition.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        toolId: { type: 'string' },
        description: { type: 'string' },
        fileName: { type: 'string' },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'tool.get_template',
    handler: async (args, ctx) => ctx.generatedToolHost.getTemplate({
      toolId: args.toolId ? String(args.toolId) : 'generated.vscode_example',
      description: args.description ? String(args.description) : 'Generated VS Code tool.',
      fileName: args.fileName ? String(args.fileName) : '',
    }),
  });
  register({
    id: 'tool.list_generated',
    description: 'List generated VS Code tool definitions currently available to the adapter.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        pageSize: { type: 'integer', minimum: 1, maximum: 200 },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'tool.list_generated',
    handler: async (args, ctx) => ctx.resultHandleStore.buildItemsResult({
      sourceToolId: 'tool.list_generated',
      fieldName: 'items',
      items: await ctx.generatedToolHost.listDefinitions(),
      summary: { kind: 'generatedTools', platformId: 'vscode' },
      pageSize: clampNumber(Number(args.pageSize) || 100, 1, 200),
    }),
  });
  register({
    id: 'tool.upsert_generated',
    description: 'Create or update one generated VS Code tool definition and reload the manifest.',
    argsSchemaJson: schemaJson({
      type: 'object',
      required: ['fileName', 'definition'],
      additionalProperties: false,
      properties: {
        fileName: { type: 'string' },
        definition: { type: 'object' },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'tool.upsert_generated',
    danger: 'high',
    requiresConfirmation: true,
    handler: async (args, ctx) => {
      const result = await ctx.generatedToolHost.upsertDefinition({
        fileName: String(args.fileName || ''),
        definition: args.definition,
      });
      await ctx.reloadGeneratedTools({ force: true });
      return {
        ...result,
        manifestHash: ctx.registry.manifestHash,
        message: 'Generated tool definition written and manifest reloaded.',
      };
    },
  });
  register({
    id: 'tool.delete_generated',
    description: 'Delete one generated VS Code tool definition and reload the manifest.',
    argsSchemaJson: schemaJson({
      type: 'object',
      required: ['fileName'],
      additionalProperties: false,
      properties: {
        fileName: { type: 'string' },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'tool.delete_generated',
    danger: 'high',
    requiresConfirmation: true,
    handler: async (args, ctx) => {
      const result = await ctx.generatedToolHost.deleteDefinition(String(args.fileName || ''));
      await ctx.reloadGeneratedTools({ force: true });
      return {
        ...result,
        manifestHash: ctx.registry.manifestHash,
      };
    },
  });
  register({
    id: 'tool.reload_generated',
    description: 'Force a generated-tool rescan and manifest rebuild.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: {} }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'tool.reload_generated',
    handler: async (_args, ctx) => ctx.reloadGeneratedTools({ force: true }),
  });
  register({
    id: 'service.logs_get',
    description: 'List recent service logs.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: { maxEntries: { type: 'integer', minimum: 1, maximum: 300 } } }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'service.logs_get',
    handler: async (args, ctx) => ctx.resultHandleStore.buildItemsResult({
      sourceToolId: 'service.logs_get',
      fieldName: 'logs',
      items: ctx.runtimeState.recentLogs(Number(args.maxEntries) || 100),
      summary: { kind: 'logs' },
      pageSize: clampNumber(Number(args.maxEntries) || 100, 1, 200),
    }),
  });
  register({
    id: 'service.recent_tool_calls_get',
    description: 'List recent tool call outcomes recorded by the adapter.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: { maxEntries: { type: 'integer', minimum: 1, maximum: 300 } } }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'service.recent_tool_calls_get',
    handler: async (args, ctx) => ctx.resultHandleStore.buildItemsResult({
      sourceToolId: 'service.recent_tool_calls_get',
      fieldName: 'calls',
      items: ctx.runtimeState.recentCalls(Number(args.maxEntries) || 100),
      summary: { kind: 'toolCalls' },
      pageSize: clampNumber(Number(args.maxEntries) || 100, 1, 200),
    }),
  });
  register({
    id: 'service.token_regenerate',
    description: 'Regenerate the adapter token and return the new token value.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: {} }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'service.token_regenerate',
    danger: 'medium',
    requiresConfirmation: true,
    handler: async (args, ctx) => ({
      token: await ctx.regenerateToken(),
      acceptedTokenHeaders: ctx.config.acceptedTokenHeaders,
    }),
  });
  register({
    id: 'vscode.workspace_summary_get',
    description: 'Return summary information about the current VS Code workspace and editor state.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: {} }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'vscode.workspace_summary_get',
    handler: async (args, ctx) => buildWorkspaceSummary(ctx),
  });
  register({
    id: 'vscode.files_list',
    description: 'List files in the current workspace using VS Code search globs.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        include: { type: 'string' },
        exclude: { type: 'string' },
        maxResults: { type: 'integer', minimum: 1, maximum: 20000 },
        pageSize: { type: 'integer', minimum: 1, maximum: 200 }
      }
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'vscode.files_list',
    handler: async (args, ctx) => {
      const include = String(args.include || '**/*');
      const exclude = String(args.exclude || '**/{node_modules,.git,Library,Temp}/**');
      const uris = await ctx.vscodeApi.workspace.findFiles(include, exclude, clampNumber(Number(args.maxResults) || 1000, 1, 20000));
      const items = uris.map((uri) => ({
        uri: uri.toString(),
        path: uri.fsPath,
        relativePath: relativeWorkspacePath(ctx, uri.fsPath),
      }));
      return ctx.resultHandleStore.buildItemsResult({
        sourceToolId: 'vscode.files_list',
        fieldName: 'files',
        items,
        summary: { kind: 'workspaceFiles', include, exclude },
        pageSize: clampNumber(Number(args.pageSize) || 100, 1, 200),
      });
    },
  });
  register({
    id: 'vscode.document_get',
    description: 'Read one workspace document by VS Code URI or file path.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        uri: { type: 'string' },
        path: { type: 'string' },
        length: { type: 'integer', minimum: 1, maximum: 32768 }
      },
      anyOf: [{ required: ['uri'] }, { required: ['path'] }]
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'vscode.document_get',
    handler: async (args, ctx) => {
      const uri = resolveUri(ctx, args);
      const text = Buffer.from(await ctx.vscodeApi.workspace.fs.readFile(uri)).toString('utf8');
      return ctx.resultHandleStore.buildTextResult({
        sourceToolId: 'vscode.document_get',
        text,
        summary: { kind: 'document', uri: uri.toString(), path: uri.fsPath },
        length: clampNumber(Number(args.length) || 4096, 1, 32768),
      });
    },
  });
  register({
    id: 'vscode.search_text',
    description: 'Search text across workspace files using the official VS Code text search API.',
    argsSchemaJson: schemaJson({
      type: 'object',
      required: ['query'],
      additionalProperties: false,
      properties: {
        query: { type: 'string' },
        include: { type: 'string' },
        exclude: { type: 'string' },
        maxResults: { type: 'integer', minimum: 1, maximum: 2000 },
        pageSize: { type: 'integer', minimum: 1, maximum: 200 }
      }
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'vscode.search_text',
    handler: async (args, ctx) => {
      const matches = await searchText(ctx, {
        query: String(args.query || ''),
        include: String(args.include || ''),
        exclude: String(args.exclude || ''),
        maxResults: clampNumber(Number(args.maxResults) || 200, 1, 2000),
      });
      return ctx.resultHandleStore.buildItemsResult({
        sourceToolId: 'vscode.search_text',
        fieldName: 'matches',
        items: matches,
        summary: { kind: 'textSearch', query: String(args.query || '') },
        pageSize: clampNumber(Number(args.pageSize) || 100, 1, 200),
      });
    },
  });
  register({
    id: 'vscode.diagnostics_list',
    description: 'List current diagnostics gathered by VS Code for workspace documents.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        severity: { type: 'string' },
        maxResults: { type: 'integer', minimum: 1, maximum: 5000 },
        pageSize: { type: 'integer', minimum: 1, maximum: 200 }
      }
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'vscode.diagnostics_list',
    handler: async (args, ctx) => {
      const severity = String(args.severity || '').toLowerCase();
      const maxResults = clampNumber(Number(args.maxResults) || 1000, 1, 5000);
      const items = [];
      for (const [uri, diagnostics] of ctx.vscodeApi.languages.getDiagnostics()) {
        for (const diagnostic of diagnostics) {
          if (severity && diagnosticSeverityName(ctx, diagnostic.severity) !== severity) {
            continue;
          }
          items.push({
            uri: uri.toString(),
            path: uri.fsPath,
            severity: diagnosticSeverityLabel(ctx, diagnostic.severity),
            code: diagnostic.code || null,
            source: diagnostic.source || null,
            message: diagnostic.message,
            range: {
              start: { line: diagnostic.range.start.line, character: diagnostic.range.start.character },
              end: { line: diagnostic.range.end.line, character: diagnostic.range.end.character },
            },
          });
          if (items.length >= maxResults) {
            break;
          }
        }
        if (items.length >= maxResults) {
          break;
        }
      }
      return ctx.resultHandleStore.buildItemsResult({
        sourceToolId: 'vscode.diagnostics_list',
        fieldName: 'diagnostics',
        items,
        summary: { kind: 'diagnostics', severity: severity || null },
        pageSize: clampNumber(Number(args.pageSize) || 100, 1, 200),
      });
    },
  });
  register({
    id: 'vscode.symbols_get',
    description: 'Return document symbols for one document using the official symbol provider pipeline.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        uri: { type: 'string' },
        path: { type: 'string' },
        pageSize: { type: 'integer', minimum: 1, maximum: 200 }
      },
      anyOf: [{ required: ['uri'] }, { required: ['path'] }]
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'vscode.symbols_get',
    handler: async (args, ctx) => {
      const uri = resolveUri(ctx, args);
      const symbols = await ctx.vscodeApi.commands.executeCommand('vscode.executeDocumentSymbolProvider', uri) || [];
      const items = flattenSymbols(symbols);
      return ctx.resultHandleStore.buildItemsResult({
        sourceToolId: 'vscode.symbols_get',
        fieldName: 'symbols',
        items,
        summary: { kind: 'documentSymbols', uri: uri.toString(), path: uri.fsPath },
        pageSize: clampNumber(Number(args.pageSize) || 100, 1, 200),
      });
    },
  });
  register({
    id: 'vscode.commands_list',
    description: 'List commands registered in the current VS Code window.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        includeInternal: { type: 'boolean' },
        pageSize: { type: 'integer', minimum: 1, maximum: 200 }
      }
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'vscode.commands_list',
    handler: async (args, ctx) => {
      const commands = await ctx.vscodeApi.commands.getCommands(Boolean(args.includeInternal));
      return ctx.resultHandleStore.buildItemsResult({
        sourceToolId: 'vscode.commands_list',
        fieldName: 'commands',
        items: commands.map((id) => ({ id })),
        summary: { kind: 'commands', includeInternal: Boolean(args.includeInternal) },
        pageSize: clampNumber(Number(args.pageSize) || 100, 1, 200),
      });
    },
  });
  register({
    id: 'vscode.tasks_list',
    description: 'List tasks available to VS Code in the current workspace.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        pageSize: { type: 'integer', minimum: 1, maximum: 200 }
      }
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'vscode.tasks_list',
    handler: async (args, ctx) => {
      const tasks = await ctx.vscodeApi.tasks.fetchTasks();
      const items = tasks.map((task) => ({
        name: task.name,
        source: task.source,
        definitionType: task.definition && task.definition.type ? task.definition.type : null,
        scope: serializeTaskScope(ctx, task.scope),
      }));
      return ctx.resultHandleStore.buildItemsResult({
        sourceToolId: 'vscode.tasks_list',
        fieldName: 'tasks',
        items,
        summary: { kind: 'tasks' },
        pageSize: clampNumber(Number(args.pageSize) || 100, 1, 200),
      });
    },
  });
  register({
    id: 'vscode.task_run',
    description: 'Execute one VS Code task by name and optional source or definition type.',
    argsSchemaJson: schemaJson({
      type: 'object',
      required: ['name'],
      additionalProperties: false,
      properties: {
        name: { type: 'string' },
        source: { type: 'string' },
        definitionType: { type: 'string' }
      }
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'vscode.task_run',
    danger: 'high',
    requiresConfirmation: true,
    handler: async (args, ctx) => {
      const task = await resolveTask(ctx, args);
      await ctx.vscodeApi.tasks.executeTask(task);
      return {
        name: task.name,
        source: task.source,
        definitionType: task.definition && task.definition.type ? task.definition.type : null,
        executed: true,
      };
    },
  });
}

function buildWorkspaceSummary(ctx) {
  const activeEditor = ctx.vscodeApi.window.activeTextEditor;
  return {
    workspaceFolders: ctx.vscodeApi.workspace.workspaceFolders
      ? ctx.vscodeApi.workspace.workspaceFolders.map((folder) => ({
        name: folder.name,
        path: folder.uri.fsPath,
        uri: folder.uri.toString(),
      }))
      : [],
    activeEditor: activeEditor
      ? {
        path: activeEditor.document.uri.fsPath,
        uri: activeEditor.document.uri.toString(),
        languageId: activeEditor.document.languageId,
        isDirty: activeEditor.document.isDirty,
      }
      : null,
    visibleEditors: ctx.vscodeApi.window.visibleTextEditors.map((editor) => ({
      path: editor.document.uri.fsPath,
      uri: editor.document.uri.toString(),
      languageId: editor.document.languageId,
      isDirty: editor.document.isDirty,
    })),
    openTextDocuments: ctx.vscodeApi.workspace.textDocuments.map((document) => ({
      path: document.uri.fsPath,
      uri: document.uri.toString(),
      languageId: document.languageId,
      isDirty: document.isDirty,
    })),
  };
}

function resolveUri(ctx, args) {
  if (args.uri) {
    return ctx.vscodeApi.Uri.parse(String(args.uri));
  }
  if (args.path) {
    const resolved = path.isAbsolute(String(args.path))
      ? String(args.path)
      : path.join(ctx.config.rootDir, String(args.path));
    return ctx.vscodeApi.Uri.file(path.resolve(resolved));
  }
  throw new Error('Provide uri or path.');
}

async function searchText(ctx, { query, include, exclude, maxResults }) {
  const items = [];
  await new Promise((resolve) => {
    let done = false;
    const options = {};
    if (include) {
      options.include = include;
    }
    if (exclude) {
      options.exclude = exclude;
    }
    if (Number.isFinite(maxResults)) {
      options.maxResults = maxResults;
    }
    ctx.vscodeApi.workspace.findTextInFiles(
      { pattern: query, isRegExp: false, isCaseSensitive: false, isWordMatch: false },
      options,
      (result) => {
        if (done) {
          return;
        }
        items.push({
          uri: result.uri.toString(),
          path: result.uri.fsPath,
          relativePath: relativeWorkspacePath(ctx, result.uri.fsPath),
          ranges: result.ranges.map((range) => ({
            preview: result.preview.text,
            start: { line: range.start.line, character: range.start.character },
            end: { line: range.end.line, character: range.end.character },
          })),
        });
        if (items.length >= maxResults) {
          done = true;
          resolve();
        }
      },
    ).then(() => resolve());
  });
  return items.slice(0, maxResults);
}

function flattenSymbols(symbols, ancestors = []) {
  const items = [];
  for (const symbol of symbols) {
    const namePath = [...ancestors, symbol.name];
    items.push({
      name: symbol.name,
      detail: symbol.detail || '',
      kind: symbol.kind,
      kindLabel: String(symbol.kind),
      namePath,
      range: serializeRange(symbol.range),
      selectionRange: serializeRange(symbol.selectionRange),
    });
    if (Array.isArray(symbol.children) && symbol.children.length > 0) {
      items.push(...flattenSymbols(symbol.children, namePath));
    }
  }
  return items;
}

function serializeRange(range) {
  return {
    start: { line: range.start.line, character: range.start.character },
    end: { line: range.end.line, character: range.end.character },
  };
}

function relativeWorkspacePath(ctx, absolutePath) {
  const folders = ctx.vscodeApi.workspace.workspaceFolders || [];
  for (const folder of folders) {
    if (absolutePath.startsWith(folder.uri.fsPath)) {
      return path.relative(folder.uri.fsPath, absolutePath);
    }
  }
  return absolutePath;
}

function diagnosticSeverityName(ctx, severity) {
  if (severity === ctx.vscodeApi.DiagnosticSeverity.Error) {
    return 'error';
  }
  if (severity === ctx.vscodeApi.DiagnosticSeverity.Warning) {
    return 'warning';
  }
  if (severity === ctx.vscodeApi.DiagnosticSeverity.Information) {
    return 'information';
  }
  return 'hint';
}

function diagnosticSeverityLabel(ctx, severity) {
  const value = diagnosticSeverityName(ctx, severity);
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function serializeTaskScope(ctx, scope) {
  if (!scope) {
    return null;
  }
  if (scope === ctx.vscodeApi.TaskScope.Global) {
    return 'global';
  }
  if (scope === ctx.vscodeApi.TaskScope.Workspace) {
    return 'workspace';
  }
  if (scope === ctx.vscodeApi.TaskScope.WorkspaceFolder) {
    return 'workspaceFolder';
  }
  if (scope.uri) {
    return scope.uri.fsPath;
  }
  return String(scope);
}

async function resolveTask(ctx, args) {
  const tasks = await ctx.vscodeApi.tasks.fetchTasks();
  const wantedName = String(args.name || '');
  const wantedSource = String(args.source || '');
  const wantedDefinitionType = String(args.definitionType || '');
  const match = tasks.find((task) => task.name === wantedName
    && (!wantedSource || task.source === wantedSource)
    && (!wantedDefinitionType || (task.definition && task.definition.type === wantedDefinitionType)));
  if (!match) {
    throw new Error(`Task not found: ${wantedName}`);
  }
  return match;
}

module.exports = {
  defaultBundles,
  registerDefaultTools,
};
