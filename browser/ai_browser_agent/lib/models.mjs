import { createHash } from 'node:crypto';
import path from 'node:path';

export const JsonMap = Object;

export const AiToolDanger = Object.freeze({
  low: 'low',
  medium: 'medium',
  high: 'high',
});

export class AiBrowserAgentConfig {
  static frameworkName = 'AI Platform Agent Framework';
  static protocolVersion = '2.0';
  static serviceId = 'aibrowser.agent';
  static serviceName = 'AI Browser Agent';
  static platformId = 'browser';
  static primaryTokenHeader = 'X-AI-Agent-Token';
  static legacyTokenHeader = 'X-Browser-Ai-Token';

  constructor({
    rootDir,
    host = '127.0.0.1',
    port = 19778,
    requireToken = true,
    fullAccessEnabled = false,
    chromeHost = '127.0.0.1',
    chromePort = 9222,
    chromeExecutable = '',
    toolTimeoutMs = 120000,
    serviceVersion = '0.1.0',
  }) {
    this.rootDir = path.resolve(rootDir);
    this.host = host;
    this.port = Number(port);
    this.requireToken = Boolean(requireToken);
    this.fullAccessEnabled = Boolean(fullAccessEnabled);
    this.chromeHost = chromeHost;
    this.chromePort = Number(chromePort);
    this.chromeExecutable = chromeExecutable;
    this.toolTimeoutMs = Number(toolTimeoutMs);
    this.serviceVersion = serviceVersion;
  }

  get acceptedTokenHeaders() {
    return [
      AiBrowserAgentConfig.primaryTokenHeader,
      AiBrowserAgentConfig.legacyTokenHeader,
    ];
  }

  get serverUrl() {
    return `http://${this.host}:${this.port}`;
  }

  get chromeBaseUrl() {
    return `http://${this.chromeHost}:${this.chromePort}`;
  }

  get stateDirectoryPath() {
    return path.join(this.rootDir, '.ai_platform_agent', 'browser');
  }

  get tokenFilePath() {
    return path.join(this.stateDirectoryPath, 'token.txt');
  }
}

export class AiRuntimeState {
  constructor() {
    this.serviceLogs = [];
    this.toolCalls = [];
  }

  log(level, message) {
    this.serviceLogs.push({
      time: new Date().toISOString(),
      level,
      message,
    });
    if (this.serviceLogs.length > 400) {
      this.serviceLogs.shift();
    }
  }

  recordCall({ toolId, ok, durationMs, message }) {
    this.toolCalls.push({
      time: new Date().toISOString(),
      toolId,
      ok,
      durationMs,
      message,
    });
    if (this.toolCalls.length > 400) {
      this.toolCalls.shift();
    }
  }

  recentLogs(maxEntries) {
    const safeMax = clampNumber(Number(maxEntries) || 100, 1, 300);
    return this.serviceLogs.slice(-safeMax);
  }

  recentCalls(maxEntries) {
    const safeMax = clampNumber(Number(maxEntries) || 100, 1, 300);
    return this.toolCalls.slice(-safeMax);
  }
}

export class AiBrowserState {
  constructor() {
    this.currentTargetId = '';
    this.lastLaunchInfo = null;
  }
}

export class AiManifestBundleDefinition {
  constructor({ id, description, prefixes }) {
    this.id = id;
    this.description = description;
    this.prefixes = prefixes;
  }
}

export class AiToolDefinition {
  constructor({
    id,
    description,
    argsSchemaJson,
    returnSchemaJson,
    handlerName,
    handler,
    danger = AiToolDanger.low,
    requiresConfirmation = false,
  }) {
    this.id = id;
    this.namespaceId = deriveNamespaceId(id);
    this.description = description;
    this.argsSchemaJson = argsSchemaJson;
    this.returnSchemaJson = returnSchemaJson;
    this.handlerName = handlerName;
    this.handler = handler;
    this.danger = danger;
    this.requiresConfirmation = requiresConfirmation;
  }

  toSummaryJson() {
    return {
      id: this.id,
      namespaceId: this.namespaceId,
      description: this.description,
      danger: this.danger,
      requiresConfirmation: this.requiresConfirmation,
    };
  }

  toFullJson() {
    return {
      id: this.id,
      namespaceId: this.namespaceId,
      description: this.description,
      argsSchemaJson: this.argsSchemaJson,
      returnSchemaJson: this.returnSchemaJson,
      danger: this.danger,
      requiresConfirmation: this.requiresConfirmation,
      handlerName: this.handlerName,
    };
  }
}

export class AiToolExecutionContext {
  constructor({
    config,
    registry,
    resultHandleStore,
    runtimeState,
    browserState,
    agentManual,
    readToken,
    regenerateToken,
    buildHealthPayload,
    buildAgentBriefPayload,
  }) {
    this.config = config;
    this.registry = registry;
    this.resultHandleStore = resultHandleStore;
    this.runtimeState = runtimeState;
    this.browserState = browserState;
    this.agentManual = agentManual;
    this.readToken = readToken;
    this.regenerateToken = regenerateToken;
    this.buildHealthPayload = buildHealthPayload;
    this.buildAgentBriefPayload = buildAgentBriefPayload;
  }
}

export class AiToolRegistry {
  constructor({ bundles }) {
    this.bundles = bundles;
    this.toolMap = new Map();
  }

  register(tool) {
    if (this.toolMap.has(tool.id)) {
      throw new Error(`Duplicate tool id: ${tool.id}`);
    }
    this.toolMap.set(tool.id, tool);
  }

  findTool(id) {
    return this.toolMap.get(id) ?? null;
  }

  get count() {
    return this.toolMap.size;
  }

  get tools() {
    return [...this.toolMap.values()].sort((left, right) => left.id.localeCompare(right.id));
  }

  get namespaceInfos() {
    const counts = new Map();
    for (const tool of this.toolMap.values()) {
      counts.set(tool.namespaceId, (counts.get(tool.namespaceId) ?? 0) + 1);
    }
    return [...counts.entries()]
      .map(([id, count]) => ({ id, count }))
      .sort((left, right) => left.id.localeCompare(right.id));
  }

  get manifestHash() {
    const hash = createHash('sha256');
    hash.update(AiBrowserAgentConfig.protocolVersion);
    for (const tool of this.tools) {
      hash.update(tool.id);
      hash.update(tool.namespaceId);
      hash.update(tool.description);
      hash.update(tool.argsSchemaJson);
      hash.update(tool.returnSchemaJson);
      hash.update(tool.danger);
      hash.update(tool.requiresConfirmation ? '1' : '0');
      hash.update(tool.handlerName);
    }
    return hash.digest('hex');
  }

  buildManifestSummary(config) {
    return {
      ok: true,
      framework: AiBrowserAgentConfig.frameworkName,
      serviceId: AiBrowserAgentConfig.serviceId,
      service: AiBrowserAgentConfig.serviceName,
      platformId: AiBrowserAgentConfig.platformId,
      version: config.serviceVersion,
      protocolVersion: AiBrowserAgentConfig.protocolVersion,
      manifestHash: this.manifestHash,
      toolCount: this.count,
      namespaces: this.namespaceInfos,
      tools: this.tools.map((tool) => tool.toSummaryJson()),
    };
  }

  buildManifestFull(config) {
    return {
      ...this.buildManifestSummary(config),
      tools: this.tools.map((tool) => tool.toFullJson()),
    };
  }

  buildBundleIndex(config) {
    return {
      ok: true,
      platformId: AiBrowserAgentConfig.platformId,
      manifestHash: this.manifestHash,
      bundles: this.bundles.map((bundle) => ({
        id: bundle.id,
        description: bundle.description,
        toolCount: this.tools.filter((tool) => matchesBundle(tool.id, bundle)).length,
      })),
    };
  }

  tryBuildBundle(config, bundleId) {
    const bundle = this.bundles.find((item) => item.id === bundleId);
    if (!bundle) {
      return null;
    }
    return {
      ok: true,
      platformId: AiBrowserAgentConfig.platformId,
      manifestHash: this.manifestHash,
      bundle: {
        id: bundle.id,
        description: bundle.description,
      },
      tools: this.tools.filter((tool) => matchesBundle(tool.id, bundle)).map((tool) => tool.toSummaryJson()),
    };
  }

  buildManifestSearch(config, { query = '', limit = 0, namespaceId = '', bundleId = '' }) {
    const safeLimit = clampNumber(Number(limit) || 8, 1, 64);
    let candidates = this.tools;
    if (namespaceId) {
      candidates = candidates.filter((tool) => tool.namespaceId === namespaceId);
    }
    if (bundleId) {
      const bundle = this.bundles.find((item) => item.id === bundleId);
      if (bundle) {
        candidates = candidates.filter((tool) => matchesBundle(tool.id, bundle));
      }
    }
    const tokens = String(query).toLowerCase().split(/\s+/).filter(Boolean);
    const scored = candidates.map((tool) => ({
      tool,
      score: searchScore(tool, tokens),
    }));
    scored.sort((left, right) => {
      if (right.score !== left.score) {
        return right.score - left.score;
      }
      return left.tool.id.localeCompare(right.tool.id);
    });
    const tools = scored
      .filter((entry) => tokens.length === 0 || entry.score > 0)
      .slice(0, safeLimit)
      .map((entry) => entry.tool.toSummaryJson());
    return {
      ok: true,
      platformId: AiBrowserAgentConfig.platformId,
      manifestHash: this.manifestHash,
      query,
      namespaceId,
      bundleId,
      returned: tools.length,
      tools,
    };
  }

  buildDescribeMany(config, ids) {
    const wanted = Array.isArray(ids) ? ids.map((item) => String(item)) : [];
    const found = [];
    const missing = [];
    for (const id of wanted) {
      const tool = this.findTool(id);
      if (tool) {
        found.push(tool.toFullJson());
      } else {
        missing.push(id);
      }
    }
    return {
      ok: true,
      platformId: AiBrowserAgentConfig.platformId,
      manifestHash: this.manifestHash,
      returned: found.length,
      missing,
      tools: found,
    };
  }
}

export function deriveNamespaceId(id) {
  const index = id.indexOf('.');
  return index < 0 ? id : id.slice(0, index);
}

export function ensureJsonObject(value) {
  if (!value || Array.isArray(value) || typeof value !== 'object') {
    return {};
  }
  return value;
}

export function clampNumber(value, min, max) {
  return Math.max(min, Math.min(max, Number.isFinite(value) ? value : min));
}

export function schemaJson(schema) {
  return JSON.stringify(schema, null, 2);
}

function matchesBundle(toolId, bundle) {
  return bundle.prefixes.some((prefix) => toolId.startsWith(prefix));
}

function searchScore(tool, tokens) {
  if (tokens.length === 0) {
    return 1;
  }
  const haystack = `${tool.id} ${tool.namespaceId} ${tool.description}`.toLowerCase();
  let score = 0;
  for (const token of tokens) {
    if (tool.id.toLowerCase().includes(token)) {
      score += 5;
    } else if (tool.description.toLowerCase().includes(token)) {
      score += 2;
    } else if (haystack.includes(token)) {
      score += 1;
    }
  }
  return score;
}
