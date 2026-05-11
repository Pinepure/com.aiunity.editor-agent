const path = require('node:path');
const { createHash } = require('node:crypto');

class AiVsCodeAgentConfig {
  static frameworkName = 'AI Platform Agent Framework';
  static protocolVersion = '2.0';
  static serviceId = 'aivscode.agent';
  static serviceName = 'AI VS Code Agent';
  static platformId = 'vscode';
  static primaryTokenHeader = 'X-AI-Agent-Token';
  static legacyTokenHeader = 'X-Vscode-Ai-Token';

  constructor({
    rootDir,
    storageDir,
    extensionDir,
    host = '127.0.0.1',
    port = 19790,
    requireToken = true,
    fullAccessEnabled = false,
    toolTimeoutMs = 120000,
    serviceVersion = '0.1.0',
  }) {
    this.rootDir = path.resolve(rootDir);
    this.storageDir = path.resolve(storageDir);
    this.extensionDir = path.resolve(extensionDir);
    this.host = host;
    this.port = Number(port);
    this.requireToken = Boolean(requireToken);
    this.fullAccessEnabled = Boolean(fullAccessEnabled);
    this.toolTimeoutMs = Number(toolTimeoutMs);
    this.serviceVersion = serviceVersion;
  }

  get acceptedTokenHeaders() {
    return [AiVsCodeAgentConfig.primaryTokenHeader, AiVsCodeAgentConfig.legacyTokenHeader];
  }

  get serverUrl() {
    return `http://${this.host}:${this.port}`;
  }

  get stateDirectoryPath() {
    return path.join(this.storageDir, 'ai_vscode_agent');
  }

  get tokenFilePath() {
    return path.join(this.stateDirectoryPath, 'token.txt');
  }

  get generatedToolsDirectoryPath() {
    return path.join(this.rootDir, '.ai_platform_agent', 'vscode', 'generated_tools');
  }
}

class AiRuntimeState {
  constructor() {
    this.serviceLogs = [];
    this.toolCalls = [];
  }

  log(level, message) {
    this.serviceLogs.push({ time: isoTimestamp(), level, message });
    if (this.serviceLogs.length > 400) {
      this.serviceLogs.shift();
    }
  }

  recordCall({ toolId, ok, durationMs, message }) {
    this.toolCalls.push({ time: isoTimestamp(), toolId, ok, durationMs, message });
    if (this.toolCalls.length > 400) {
      this.toolCalls.shift();
    }
  }

  recentLogs(maxEntries) {
    const safeMax = clampNumber(maxEntries || 100, 1, 300);
    return this.serviceLogs.slice(-safeMax);
  }

  recentCalls(maxEntries) {
    const safeMax = clampNumber(maxEntries || 100, 1, 300);
    return this.toolCalls.slice(-safeMax);
  }
}

class AiManifestBundleDefinition {
  constructor({ id, description, prefixes }) {
    this.id = id;
    this.description = description;
    this.prefixes = prefixes;
  }
}

class AiToolDefinition {
  constructor({ id, description, argsSchemaJson, returnSchemaJson, handlerName, handler, danger = 'low', requiresConfirmation = false }) {
    this.id = id;
    this.description = description;
    this.argsSchemaJson = argsSchemaJson;
    this.returnSchemaJson = returnSchemaJson;
    this.handlerName = handlerName;
    this.handler = handler;
    this.danger = danger;
    this.requiresConfirmation = requiresConfirmation;
    this.namespaceId = deriveNamespaceId(id);
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

class AiToolExecutionContext {
  constructor(payload) {
    Object.assign(this, payload);
  }
}

class AiToolRegistry {
  constructor({ bundles }) {
    this.bundles = bundles;
    this.toolsById = new Map();
  }

  register(tool) {
    if (this.toolsById.has(tool.id)) {
      throw new Error(`Duplicate tool id: ${tool.id}`);
    }
    this.toolsById.set(tool.id, tool);
  }

  findTool(toolId) {
    return this.toolsById.get(toolId) || null;
  }

  get tools() {
    return [...this.toolsById.values()].sort((left, right) => left.id.localeCompare(right.id));
  }

  get count() {
    return this.toolsById.size;
  }

  get namespaceInfos() {
    const counts = new Map();
    for (const tool of this.toolsById.values()) {
      counts.set(tool.namespaceId, (counts.get(tool.namespaceId) || 0) + 1);
    }
    return [...counts.entries()]
      .sort((left, right) => left[0].localeCompare(right[0]))
      .map(([id, count]) => ({ id, count }));
  }

  get manifestHash() {
    const digest = createHash('sha256');
    digest.update(AiVsCodeAgentConfig.protocolVersion);
    for (const tool of this.tools) {
      digest.update(tool.id);
      digest.update(tool.namespaceId);
      digest.update(tool.description);
      digest.update(tool.argsSchemaJson);
      digest.update(tool.returnSchemaJson);
      digest.update(tool.danger);
      digest.update(tool.requiresConfirmation ? '1' : '0');
      digest.update(tool.handlerName);
    }
    return digest.digest('hex');
  }

  buildManifestSummary(config) {
    return {
      ok: true,
      framework: AiVsCodeAgentConfig.frameworkName,
      serviceId: AiVsCodeAgentConfig.serviceId,
      service: AiVsCodeAgentConfig.serviceName,
      platformId: AiVsCodeAgentConfig.platformId,
      version: config.serviceVersion,
      protocolVersion: AiVsCodeAgentConfig.protocolVersion,
      manifestHash: this.manifestHash,
      toolCount: this.count,
      namespaces: this.namespaceInfos,
      tools: this.tools.map((tool) => tool.toSummaryJson()),
    };
  }

  buildManifestFull(config) {
    const payload = this.buildManifestSummary(config);
    payload.tools = this.tools.map((tool) => tool.toFullJson());
    return payload;
  }

  buildBundleIndex() {
    return {
      ok: true,
      platformId: AiVsCodeAgentConfig.platformId,
      manifestHash: this.manifestHash,
      bundles: this.bundles.map((bundle) => ({
        id: bundle.id,
        description: bundle.description,
        toolCount: this.tools.filter((tool) => matchesBundle(tool.id, bundle)).length,
      })),
    };
  }

  tryBuildBundle(bundleId) {
    const bundle = this.bundles.find((item) => item.id === bundleId);
    if (!bundle) {
      return null;
    }
    return {
      ok: true,
      platformId: AiVsCodeAgentConfig.platformId,
      manifestHash: this.manifestHash,
      bundle: {
        id: bundle.id,
        description: bundle.description,
      },
      tools: this.tools.filter((tool) => matchesBundle(tool.id, bundle)).map((tool) => tool.toSummaryJson()),
    };
  }

  buildManifestSearch({ query = '', limit = 0, namespaceId = '', bundleId = '' }) {
    const safeLimit = clampNumber(limit || 8, 1, 64);
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
    const scored = candidates
      .map((tool) => [tool, searchScore(tool, tokens)])
      .sort((left, right) => right[1] - left[1] || left[0].id.localeCompare(right[0].id));
    const tools = scored
      .filter(([, score]) => tokens.length === 0 || score > 0)
      .slice(0, safeLimit)
      .map(([tool]) => tool.toSummaryJson());
    return {
      ok: true,
      platformId: AiVsCodeAgentConfig.platformId,
      manifestHash: this.manifestHash,
      query,
      namespaceId,
      bundleId,
      returned: tools.length,
      tools,
    };
  }

  buildDescribeMany(ids) {
    const found = [];
    const missing = [];
    for (const id of ids) {
      const tool = this.findTool(String(id));
      if (!tool) {
        missing.push(String(id));
      } else {
        found.push(tool.toFullJson());
      }
    }
    return {
      ok: true,
      platformId: AiVsCodeAgentConfig.platformId,
      manifestHash: this.manifestHash,
      returned: found.length,
      missing,
      tools: found,
    };
  }
}

function clampNumber(value, minimum, maximum) {
  return Math.max(minimum, Math.min(maximum, Number(value)));
}

function deriveNamespaceId(toolId) {
  return String(toolId).split('.', 1)[0];
}

function schemaJson(schema) {
  return JSON.stringify(schema, null, 2);
}

function ensureJsonObject(value) {
  return value && typeof value === 'object' && !Array.isArray(value) ? value : {};
}

function matchesBundle(toolId, bundle) {
  return bundle.prefixes.some((prefix) => toolId.startsWith(prefix));
}

function searchScore(tool, tokens) {
  if (tokens.length === 0) {
    return 1;
  }
  const haystack = `${tool.id} ${tool.namespaceId} ${tool.description}`.toLowerCase();
  const description = tool.description.toLowerCase();
  const toolId = tool.id.toLowerCase();
  let score = 0;
  for (const token of tokens) {
    if (toolId.includes(token)) {
      score += 5;
    } else if (description.includes(token)) {
      score += 2;
    } else if (haystack.includes(token)) {
      score += 1;
    }
  }
  return score;
}

function isoTimestamp() {
  return new Date().toISOString();
}

module.exports = {
  AiManifestBundleDefinition,
  AiRuntimeState,
  AiToolDefinition,
  AiToolExecutionContext,
  AiToolRegistry,
  AiVsCodeAgentConfig,
  clampNumber,
  ensureJsonObject,
  schemaJson,
};
