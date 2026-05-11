const fs = require('node:fs/promises');
const { readFileSync } = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const crypto = require('node:crypto');

const {
  AiRuntimeState,
  AiToolExecutionContext,
  AiToolRegistry,
  AiVsCodeAgentConfig,
  clampNumber,
  ensureJsonObject,
} = require('./models');
const { ResultHandleStore } = require('./result_handle_store');
const { defaultBundles, registerDefaultTools } = require('./tools');
const { VsCodeGeneratedToolHost } = require('./generated_tool_host');

class AiVsCodeAgentServer {
  constructor(config, extensionContext, vscodeApi) {
    this.config = config;
    this.extensionContext = extensionContext;
    this.vscodeApi = vscodeApi;
    this.runtimeState = new AiRuntimeState();
    this.resultHandleStore = new ResultHandleStore();
    this.registry = new AiToolRegistry({ bundles: defaultBundles() });
    this.generatedToolHost = new VsCodeGeneratedToolHost({
      config: this.config,
      vscodeApi: this.vscodeApi,
      runtimeState: this.runtimeState,
      resultHandleStore: this.resultHandleStore,
    });
    this.server = null;
    this.generatedToolsFingerprint = '';
    this.generatedToolsDirty = true;
    this.generatedToolsCount = 0;
    this.token = '';
    this.agentManual = readFileSync(path.resolve(this.config.extensionDir, 'AGENT.md'), 'utf8');
    this.toolContext = new AiToolExecutionContext({
      config: this.config,
      registry: this.registry,
      resultHandleStore: this.resultHandleStore,
      runtimeState: this.runtimeState,
      vscodeApi: this.vscodeApi,
      extensionContext: this.extensionContext,
      agentManual: this.agentManual,
      readToken: () => this.token,
      regenerateToken: async () => this.regenerateToken(),
      buildHealthPayload: ({ includeOk = false } = {}) => this.buildHealthPayload({ includeOk }),
      buildAgentBriefPayload: ({ includeOk = false } = {}) => this.buildAgentBriefPayload({ includeOk }),
      generatedToolHost: this.generatedToolHost,
      reloadGeneratedTools: async ({ force = false } = {}) => this.refreshRegistry({ force }),
    });
  }

  get isRunning() {
    return this.server !== null;
  }

  async start() {
    if (this.isRunning) {
      return;
    }
    await this.refreshRegistry({ force: true });
    if (this.config.requireToken) {
      this.token = await this.ensureToken();
    }
    this.server = http.createServer((request, response) => {
      this.handleRequest(request, response).catch(async (error) => {
        this.runtimeState.log('error', String(error));
        await this.sendError(response, 500, String(error));
      });
    });
    await new Promise((resolve, reject) => {
      this.server.on('error', reject);
      this.server.listen(this.config.port, this.config.host, resolve);
    });
    this.runtimeState.log('info', `Service started at ${this.config.serverUrl}`);
  }

  async stop() {
    if (!this.server) {
      return;
    }
    const server = this.server;
    this.server = null;
    await new Promise((resolve, reject) => {
      server.close((error) => {
        if (error) {
          reject(error);
        } else {
          resolve();
        }
      });
    });
  }

  markGeneratedToolsDirty(reason = '') {
    this.generatedToolsDirty = true;
    if (reason) {
      this.runtimeState.log('info', `Generated tool registry marked dirty: ${reason}`);
    }
  }

  async handleRequest(request, response) {
    await this.refreshRegistry();
    this.addCommonHeaders(response);
    if (request.method === 'OPTIONS') {
      response.writeHead(204);
      response.end();
      return;
    }

    const parsedUrl = new URL(request.url, this.config.serverUrl);
    const pathName = parsedUrl.pathname.replace(/^\/+/, '');

    if (pathName === 'health' && request.method === 'GET') {
      await this.sendJson(response, 200, this.buildHealthPayload({ includeOk: true }));
      return;
    }

    if (!this.isAuthorized(request)) {
      await this.sendError(
        response,
        401,
        `Unauthorized. Provide ${AiVsCodeAgentConfig.primaryTokenHeader} or ${AiVsCodeAgentConfig.legacyTokenHeader}.`,
      );
      return;
    }

    if ((pathName === 'manifest' || pathName === 'manifest/summary') && request.method === 'GET') {
      const detail = parsedUrl.searchParams.get('detail') || '';
      const payload = detail.toLowerCase() === 'full'
        ? this.registry.buildManifestFull(this.config)
        : this.registry.buildManifestSummary(this.config);
      await this.sendJson(response, 200, payload);
      return;
    }

    if (pathName === 'manifest/full' && request.method === 'GET') {
      await this.sendJson(response, 200, this.registry.buildManifestFull(this.config));
      return;
    }

    if (pathName === 'manifest/bundles' && request.method === 'GET') {
      await this.sendJson(response, 200, this.registry.buildBundleIndex(this.config));
      return;
    }

    if (pathName.startsWith('manifest/bundle/') && request.method === 'GET') {
      const bundleId = decodeURIComponent(pathName.slice('manifest/bundle/'.length));
      const payload = this.registry.tryBuildBundle(bundleId);
      if (!payload) {
        await this.sendError(response, 404, `Unknown manifest bundle: ${bundleId}`);
        return;
      }
      await this.sendJson(response, 200, payload);
      return;
    }

    if (pathName === 'manifest/search' && request.method === 'POST') {
      const body = await this.readBodyAsJson(request);
      const payload = this.registry.buildManifestSearch({
        query: body.query ? String(body.query) : '',
        limit: body.limit,
        namespaceId: body.namespaceId ? String(body.namespaceId) : '',
        bundleId: body.bundleId ? String(body.bundleId) : '',
      });
      await this.sendJson(response, 200, payload);
      return;
    }

    if (pathName === 'tool/describe_many' && request.method === 'POST') {
      const body = await this.readBodyAsJson(request);
      await this.sendJson(response, 200, this.registry.buildDescribeMany(Array.isArray(body.ids) ? body.ids : []));
      return;
    }

    if (pathName === 'agent/brief' && request.method === 'GET') {
      await this.sendJson(response, 200, this.buildAgentBriefPayload({ includeOk: true }));
      return;
    }

    if (pathName === 'agent' && request.method === 'GET') {
      await this.sendJson(response, 200, { ok: true, content: this.agentManual });
      return;
    }

    if (pathName.startsWith('result/') && request.method === 'GET') {
      const handleId = decodeURIComponent(pathName.slice('result/'.length));
      const payload = this.resultHandleStore.buildPage(handleId, {
        offset: clampNumber(Number(parsedUrl.searchParams.get('offset')) || 0, 0, Number.MAX_SAFE_INTEGER),
        limit: clampNumber(Number(parsedUrl.searchParams.get('limit')) || 0, 0, 32768),
      });
      if (!payload) {
        await this.sendError(response, 404, `Unknown result handle: ${handleId}`);
        return;
      }
      await this.sendJson(response, 200, payload);
      return;
    }

    if (pathName.startsWith('call/') && request.method === 'POST') {
      const toolId = decodeURIComponent(pathName.slice('call/'.length));
      const tool = this.registry.findTool(toolId);
      if (!tool) {
        await this.sendJson(response, 404, { ok: false, toolId, error: `Unknown tool: ${toolId}` });
        return;
      }
      if (tool.requiresConfirmation && !this.config.fullAccessEnabled) {
        await this.sendJson(response, 403, {
          ok: false,
          toolId,
          error: 'High-risk tool requires aiPlatformAgent.vscode.fullAccess.',
        });
        return;
      }
      const body = await this.readBodyAsJson(request);
      const startedAt = Date.now();
      try {
        const result = await Promise.race([
          tool.handler(body, this.toolContext),
          timeoutAfter(this.config.toolTimeoutMs),
        ]);
        this.runtimeState.recordCall({
          toolId,
          ok: true,
          durationMs: Date.now() - startedAt,
          message: 'ok',
        });
        await this.sendJson(response, 200, {
          ok: true,
          toolId,
          durationMs: Date.now() - startedAt,
          result,
        });
      } catch (error) {
        this.runtimeState.recordCall({
          toolId,
          ok: false,
          durationMs: Date.now() - startedAt,
          message: String(error),
        });
        await this.sendJson(response, 500, {
          ok: false,
          toolId,
          durationMs: Date.now() - startedAt,
          error: String(error),
        });
      }
      return;
    }

    await this.sendError(response, 404, `Not found: ${pathName}`);
  }

  isAuthorized(request) {
    if (!this.config.requireToken) {
      return true;
    }
    const provided = request.headers[AiVsCodeAgentConfig.primaryTokenHeader.toLowerCase()]
      || request.headers[AiVsCodeAgentConfig.legacyTokenHeader.toLowerCase()];
    return Boolean(provided && provided === this.token);
  }

  async ensureToken() {
    await fs.mkdir(this.config.stateDirectoryPath, { recursive: true });
    try {
      const token = (await fs.readFile(this.config.tokenFilePath, 'utf8')).trim();
      if (token) {
        return token;
      }
    } catch {
      return this.regenerateToken();
    }
    return this.regenerateToken();
  }

  async regenerateToken() {
    const token = crypto.randomBytes(24).toString('hex');
    await fs.mkdir(this.config.stateDirectoryPath, { recursive: true });
    await fs.writeFile(this.config.tokenFilePath, token, 'utf8');
    this.token = token;
    this.runtimeState.log('info', 'Token regenerated.');
    return token;
  }

  buildHealthPayload({ includeOk = false } = {}) {
    return {
      ...(includeOk ? { ok: true } : {}),
      framework: AiVsCodeAgentConfig.frameworkName,
      service: AiVsCodeAgentConfig.serviceName,
      serviceId: AiVsCodeAgentConfig.serviceId,
      version: this.config.serviceVersion,
      platformId: AiVsCodeAgentConfig.platformId,
      protocolVersion: AiVsCodeAgentConfig.protocolVersion,
      serverRunning: this.isRunning,
      requiresToken: this.config.requireToken,
      acceptedTokenHeaders: this.config.acceptedTokenHeaders,
      serverUrl: this.config.serverUrl,
      manifestHash: this.registry.manifestHash,
      toolCount: this.registry.count,
      namespaces: this.registry.namespaceInfos,
      supportsManifestSearch: true,
      supportsDescribeMany: true,
      supportsResultHandles: true,
      supportsBundles: true,
      supportsTextChunking: true,
      supportsDynamicToolRegistration: true,
      recommendedFlow: [
        'GET /health and compare manifestHash before refreshing capabilities.',
        'Use POST /manifest/search or GET /manifest/bundle/{id} to narrow candidate tools.',
        'Use POST /tool/describe_many for exact argument and return schemas before calling tools.',
        'Use GET /result/{handleId} for additional pages or text chunks when a tool returns a resultHandle.',
        'Use GET /manifest/full only as a fallback when search is insufficient.',
      ],
      paths: {
        health: '/health',
        manifestSummary: '/manifest',
        manifestFull: '/manifest/full',
        manifestSearch: '/manifest/search',
        manifestBundles: '/manifest/bundles',
        toolDescribeMany: '/tool/describe_many',
        call: '/call/{toolId}',
        agent: '/agent',
        agentBrief: '/agent/brief',
        resultPage: '/result/{handleId}',
      },
      platform: {
        rootDir: this.config.rootDir,
        storageDir: this.config.storageDir,
        tokenFilePath: this.config.tokenFilePath,
        generatedToolsDirectoryPath: this.config.generatedToolsDirectoryPath,
        generatedToolCount: this.generatedToolsCount,
        workspaceFolders: this.vscodeApi.workspace.workspaceFolders
          ? this.vscodeApi.workspace.workspaceFolders.map((folder) => ({
            name: folder.name,
            path: folder.uri.fsPath,
          }))
          : [],
      },
    };
  }

  buildAgentBriefPayload({ includeOk = false } = {}) {
    return {
      ...(includeOk ? { ok: true } : {}),
      framework: AiVsCodeAgentConfig.frameworkName,
      platformId: AiVsCodeAgentConfig.platformId,
      summary: 'Use VS Code workspace, search, diagnostics, symbol, command, and task tools through the shared discovery-first protocol instead of inferring IDE state from files alone.',
      steps: [
        'Call GET /health and reuse cached capabilities while manifestHash stays unchanged.',
        'Search tools with POST /manifest/search or load a focused bundle with GET /manifest/bundle/{id}.',
        'Inspect workspace, files, diagnostics, and symbols before executing tasks.',
        'Request exact tool schemas with POST /tool/describe_many before calling unfamiliar tools.',
        'When a tool returns resultHandle, page additional data through GET /result/{handleId}.',
      ],
      paths: {
        health: '/health',
        manifestSummary: '/manifest',
        manifestFull: '/manifest/full',
        manifestSearch: '/manifest/search',
        manifestBundles: '/manifest/bundles',
        toolDescribeMany: '/tool/describe_many',
        call: '/call/{toolId}',
        agent: '/agent',
        agentBrief: '/agent/brief',
        resultPage: '/result/{handleId}',
      },
    };
  }

  async readBodyAsJson(request) {
    const chunks = [];
    for await (const chunk of request) {
      chunks.push(chunk);
    }
    if (chunks.length === 0) {
      return {};
    }
    return ensureJsonObject(JSON.parse(Buffer.concat(chunks).toString('utf8')));
  }

  addCommonHeaders(response) {
    response.setHeader('Access-Control-Allow-Origin', '*');
    response.setHeader('Access-Control-Allow-Headers', 'Content-Type, X-AI-Agent-Token, X-Vscode-Ai-Token');
    response.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    response.setHeader('Content-Type', 'application/json; charset=utf-8');
  }

  async sendJson(response, statusCode, payload) {
    const body = JSON.stringify(payload, null, 2);
    response.writeHead(statusCode, { 'Content-Length': Buffer.byteLength(body) });
    response.end(body);
  }

  async sendError(response, statusCode, error) {
    await this.sendJson(response, statusCode, { ok: false, error });
  }

  async refreshRegistry({ force = false } = {}) {
    if (!force && !this.generatedToolsDirty && this.registry.count > 0) {
      return {
        reloaded: false,
        manifestHash: this.registry.manifestHash,
        generatedToolCount: this.generatedToolsCount,
      };
    }

    const fingerprint = await this.generatedToolHost.computeFingerprint();
    if (!force && this.generatedToolsFingerprint === fingerprint && this.registry.count > 0) {
      this.generatedToolsDirty = false;
      return {
        reloaded: false,
        manifestHash: this.registry.manifestHash,
        generatedToolCount: this.generatedToolsCount,
      };
    }

    const nextRegistry = new AiToolRegistry({ bundles: defaultBundles() });
    this.toolContext.registry = nextRegistry;
    registerDefaultTools(nextRegistry, this.toolContext);
    const generatedDefinitions = await this.generatedToolHost.loadDefinitions();
    for (const definition of generatedDefinitions) {
      nextRegistry.register(this.generatedToolHost.createToolDefinition(definition, this.toolContext));
    }

    this.registry = nextRegistry;
    this.toolContext.registry = nextRegistry;
    this.generatedToolsFingerprint = fingerprint;
    this.generatedToolsDirty = false;
    this.generatedToolsCount = generatedDefinitions.length;
    this.runtimeState.log('info', `Generated tool registry refreshed. Loaded ${generatedDefinitions.length} generated tool(s).`);
    return {
      reloaded: true,
      manifestHash: this.registry.manifestHash,
      generatedToolCount: generatedDefinitions.length,
    };
  }
}

function timeoutAfter(timeoutMs) {
  return new Promise((_, reject) => {
    setTimeout(() => reject(new Error(`Tool timed out after ${timeoutMs}ms.`)), timeoutMs);
  });
}

module.exports = {
  AiVsCodeAgentServer,
};
