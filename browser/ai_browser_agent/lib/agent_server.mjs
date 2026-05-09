import fs from 'node:fs/promises';
import { readFileSync } from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { randomBytes } from 'node:crypto';
import { fileURLToPath } from 'node:url';

import {
  AiBrowserAgentConfig,
  AiBrowserState,
  AiRuntimeState,
  AiToolExecutionContext,
  AiToolRegistry,
  clampNumber,
  ensureJsonObject,
} from './models.mjs';
import { ResultHandleStore } from './result_handle_store.mjs';
import { defaultBundles, registerDefaultTools } from './tools.mjs';

export class AiBrowserAgentServer {
  constructor(config) {
    this.config = config;
    this.runtimeState = new AiRuntimeState();
    this.resultHandleStore = new ResultHandleStore();
    this.browserState = new AiBrowserState();
    this.registry = new AiToolRegistry({ bundles: defaultBundles() });
    this.server = null;
    this.initialized = false;
    this.token = '';
    this.agentManual = readFileSync(
      path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'AGENT.md'),
      'utf8',
    );
    this.toolContext = new AiToolExecutionContext({
      config: this.config,
      registry: this.registry,
      resultHandleStore: this.resultHandleStore,
      runtimeState: this.runtimeState,
      browserState: this.browserState,
      agentManual: this.agentManual,
      readToken: () => this.token,
      regenerateToken: async () => this.#regenerateToken(),
      buildHealthPayload: ({ includeOk = false } = {}) => this.#buildHealthPayload({ includeOk }),
      buildAgentBriefPayload: ({ includeOk = false } = {}) => this.#buildAgentBriefPayload({ includeOk }),
    });
  }

  get isRunning() {
    return this.server !== null;
  }

  async start() {
    if (this.isRunning) {
      return;
    }
    if (!this.initialized) {
      registerDefaultTools(this.registry, this.toolContext);
      this.initialized = true;
    }
    if (this.config.requireToken) {
      this.token = await this.#ensureToken();
    }
    this.server = http.createServer((request, response) => {
      this.#handleRequest(request, response).catch(async (error) => {
        this.runtimeState.log('error', String(error));
        await this.#sendError(response, 500, String(error));
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

  async #handleRequest(request, response) {
    this.#addCommonHeaders(response);
    if (request.method === 'OPTIONS') {
      response.writeHead(204);
      response.end();
      return;
    }

    const parsedUrl = new URL(request.url, this.config.serverUrl);
    const pathName = parsedUrl.pathname.replace(/^\/+/, '');

    if (pathName === 'health' && request.method === 'GET') {
      await this.#sendJson(response, 200, this.#buildHealthPayload({ includeOk: true }));
      return;
    }

    if (!this.#isAuthorized(request)) {
      await this.#sendError(
        response,
        401,
        `Unauthorized. Provide ${AiBrowserAgentConfig.primaryTokenHeader} or ${AiBrowserAgentConfig.legacyTokenHeader}.`,
      );
      return;
    }

    if ((pathName === 'manifest' || pathName === 'manifest/summary') && request.method === 'GET') {
      const detail = parsedUrl.searchParams.get('detail') ?? '';
      const payload = detail.toLowerCase() === 'full'
        ? this.registry.buildManifestFull(this.config)
        : this.registry.buildManifestSummary(this.config);
      await this.#sendJson(response, 200, payload);
      return;
    }

    if (pathName === 'manifest/full' && request.method === 'GET') {
      await this.#sendJson(response, 200, this.registry.buildManifestFull(this.config));
      return;
    }

    if (pathName === 'manifest/bundles' && request.method === 'GET') {
      await this.#sendJson(response, 200, this.registry.buildBundleIndex(this.config));
      return;
    }

    if (pathName.startsWith('manifest/bundle/') && request.method === 'GET') {
      const bundleId = decodeURIComponent(pathName.slice('manifest/bundle/'.length));
      const payload = this.registry.tryBuildBundle(this.config, bundleId);
      if (!payload) {
        await this.#sendError(response, 404, `Unknown manifest bundle: ${bundleId}`);
        return;
      }
      await this.#sendJson(response, 200, payload);
      return;
    }

    if (pathName === 'manifest/search' && request.method === 'POST') {
      const body = await this.#readBodyAsJson(request);
      const payload = this.registry.buildManifestSearch(this.config, {
        query: body.query ? String(body.query) : '',
        limit: body.limit,
        namespaceId: body.namespaceId ? String(body.namespaceId) : '',
        bundleId: body.bundleId ? String(body.bundleId) : '',
      });
      await this.#sendJson(response, 200, payload);
      return;
    }

    if (pathName === 'tool/describe_many' && request.method === 'POST') {
      const body = await this.#readBodyAsJson(request);
      await this.#sendJson(
        response,
        200,
        this.registry.buildDescribeMany(this.config, Array.isArray(body.ids) ? body.ids : []),
      );
      return;
    }

    if (pathName === 'agent/brief' && request.method === 'GET') {
      await this.#sendJson(response, 200, this.#buildAgentBriefPayload({ includeOk: true }));
      return;
    }

    if (pathName === 'agent' && request.method === 'GET') {
      await this.#sendJson(response, 200, {
        ok: true,
        content: this.agentManual,
      });
      return;
    }

    if (pathName.startsWith('result/') && request.method === 'GET') {
      const handleId = decodeURIComponent(pathName.slice('result/'.length));
      const payload = this.resultHandleStore.buildPage(handleId, {
        offset: clampNumber(Number(parsedUrl.searchParams.get('offset')) || 0, 0, Number.MAX_SAFE_INTEGER),
        limit: clampNumber(Number(parsedUrl.searchParams.get('limit')) || 0, 0, 32768),
      });
      if (!payload) {
        await this.#sendError(response, 404, `Unknown result handle: ${handleId}`);
        return;
      }
      await this.#sendJson(response, 200, payload);
      return;
    }

    if (pathName.startsWith('call/') && request.method === 'POST') {
      const toolId = decodeURIComponent(pathName.slice('call/'.length));
      const tool = this.registry.findTool(toolId);
      if (!tool) {
        await this.#sendJson(response, 404, {
          ok: false,
          toolId,
          error: `Unknown tool: ${toolId}`,
        });
        return;
      }
      if (tool.requiresConfirmation && !this.config.fullAccessEnabled) {
        await this.#sendJson(response, 403, {
          ok: false,
          toolId,
          error: 'High-risk tool requires --full-access for this adapter.',
        });
        return;
      }
      const body = await this.#readBodyAsJson(request);
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
        await this.#sendJson(response, 200, {
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
        await this.#sendJson(response, 500, {
          ok: false,
          toolId,
          durationMs: Date.now() - startedAt,
          error: String(error),
        });
      }
      return;
    }

    await this.#sendError(response, 404, `Not found: ${pathName}`);
  }

  #isAuthorized(request) {
    if (!this.config.requireToken) {
      return true;
    }
    const provided = request.headers[AiBrowserAgentConfig.primaryTokenHeader.toLowerCase()]
      ?? request.headers[AiBrowserAgentConfig.legacyTokenHeader.toLowerCase()];
    return typeof provided === 'string' && provided === this.token;
  }

  async #readBodyAsJson(request) {
    const chunks = [];
    for await (const chunk of request) {
      chunks.push(chunk);
    }
    const content = Buffer.concat(chunks).toString('utf8');
    if (!content.trim()) {
      return {};
    }
    return ensureJsonObject(JSON.parse(content));
  }

  #buildHealthPayload({ includeOk = false } = {}) {
    return {
      ...(includeOk ? { ok: true } : {}),
      framework: AiBrowserAgentConfig.frameworkName,
      service: AiBrowserAgentConfig.serviceName,
      serviceId: AiBrowserAgentConfig.serviceId,
      version: this.config.serviceVersion,
      platformId: AiBrowserAgentConfig.platformId,
      protocolVersion: AiBrowserAgentConfig.protocolVersion,
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
      supportsDynamicToolRegistration: false,
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
        chromeBaseUrl: this.config.chromeBaseUrl,
        currentTargetId: this.browserState.currentTargetId || null,
        lastLaunchInfo: this.browserState.lastLaunchInfo,
      },
    };
  }

  #buildAgentBriefPayload({ includeOk = false } = {}) {
    return {
      ...(includeOk ? { ok: true } : {}),
      framework: AiBrowserAgentConfig.frameworkName,
      platformId: AiBrowserAgentConfig.platformId,
      summary: 'Prefer cached discovery through health, manifest search, on-demand tool descriptions, and paged result handles instead of repeatedly loading the full manifest.',
      steps: [
        'Call GET /health and reuse cached capabilities while manifestHash stays unchanged.',
        'Search tools with POST /manifest/search or load a focused bundle with GET /manifest/bundle/{id}.',
        'Request exact tool schemas with POST /tool/describe_many before calling unfamiliar tools.',
        'When a tool returns resultHandle, page additional data through GET /result/{handleId} instead of re-running the tool with larger limits.',
        'Fallback to GET /manifest/full or GET /agent only when the optimized discovery flow is insufficient.',
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

  async #ensureToken() {
    await fs.mkdir(path.dirname(this.config.tokenFilePath), { recursive: true });
    try {
      const existing = (await fs.readFile(this.config.tokenFilePath, 'utf8')).trim();
      if (existing) {
        return existing;
      }
    } catch {
      // create below
    }
    return this.#regenerateToken();
  }

  async #regenerateToken() {
    const token = randomBytes(24).toString('hex');
    await fs.mkdir(path.dirname(this.config.tokenFilePath), { recursive: true });
    await fs.writeFile(this.config.tokenFilePath, token, 'utf8');
    this.token = token;
    this.runtimeState.log('info', 'Token regenerated.');
    return token;
  }

  #addCommonHeaders(response) {
    response.setHeader('Access-Control-Allow-Origin', '*');
    response.setHeader('Access-Control-Allow-Headers', 'Content-Type, X-AI-Agent-Token, X-Browser-Ai-Token');
    response.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    response.setHeader('Content-Type', 'application/json; charset=utf-8');
  }

  async #sendJson(response, statusCode, payload) {
    response.writeHead(statusCode);
    response.end(JSON.stringify(payload, null, 2));
  }

  async #sendError(response, statusCode, message) {
    await this.#sendJson(response, statusCode, {
      ok: false,
      error: message,
    });
  }
}

function timeoutAfter(timeoutMs) {
  return new Promise((_, reject) => {
    setTimeout(() => reject(new Error(`Tool timed out after ${timeoutMs}ms`)), timeoutMs);
  });
}
