import fs from 'node:fs/promises';
import path from 'node:path';

import {
  activateTarget,
  ChromeSession,
  closeTarget,
  createTarget,
  getChromeVersion,
  launchChrome,
  listTargets,
} from './chrome_client.mjs';
import {
  AiManifestBundleDefinition,
  AiToolDanger,
  AiToolDefinition,
  clampNumber,
  schemaJson,
} from './models.mjs';

export function defaultBundles() {
  return [
    new AiManifestBundleDefinition({
      id: 'service',
      description: 'Adapter health, logs, config, and recent calls.',
      prefixes: ['service.'],
    }),
    new AiManifestBundleDefinition({
      id: 'browser.targets',
      description: 'Chrome connectivity, target discovery, and page lifecycle tools.',
      prefixes: ['browser.chrome_', 'browser.targets_', 'browser.page_open', 'browser.page_activate', 'browser.page_close', 'browser.current_target_'],
    }),
    new AiManifestBundleDefinition({
      id: 'browser.inspect',
      description: 'Runtime evaluation, DOM inspection, and screenshot capture.',
      prefixes: ['browser.page_navigate', 'browser.page_reload', 'browser.runtime_', 'browser.dom_', 'browser.page_capture_'],
    }),
  ];
}

export function registerDefaultTools(registry, context) {
  const register = (definition) => registry.register(new AiToolDefinition(definition));

  register({
    id: 'service.health_get',
    description: 'Return the adapter health payload.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: {} }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'service.health_get',
    handler: async () => context.buildHealthPayload({ includeOk: true }),
  });

  register({
    id: 'service.agent_brief_get',
    description: 'Return the concise operating brief for this adapter.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: {} }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'service.agent_brief_get',
    handler: async () => context.buildAgentBriefPayload({ includeOk: true }),
  });

  register({
    id: 'service.config_get',
    description: 'Return effective adapter configuration, token header names, and the current target state.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: {} }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'service.config_get',
    handler: async () => ({
      rootDir: context.config.rootDir,
      requireToken: context.config.requireToken,
      fullAccessEnabled: context.config.fullAccessEnabled,
      acceptedTokenHeaders: context.config.acceptedTokenHeaders,
      chromeBaseUrl: context.config.chromeBaseUrl,
      currentTargetId: context.browserState.currentTargetId || null,
      lastLaunchInfo: context.browserState.lastLaunchInfo,
    }),
  });

  register({
    id: 'service.logs_get',
    description: 'List recent service logs.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        maxEntries: { type: 'integer', minimum: 1, maximum: 300 },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'service.logs_get',
    handler: async (args) => context.resultHandleStore.buildItemsResult({
      sourceToolId: 'service.logs_get',
      fieldName: 'logs',
      items: context.runtimeState.recentLogs(args.maxEntries),
      summary: {
        kind: 'logs',
      },
      pageSize: clampNumber(Number(args.maxEntries) || 100, 1, 200),
    }),
  });

  register({
    id: 'service.recent_tool_calls_get',
    description: 'List recent tool call outcomes recorded by the adapter.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        maxEntries: { type: 'integer', minimum: 1, maximum: 300 },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'service.recent_tool_calls_get',
    handler: async (args) => context.resultHandleStore.buildItemsResult({
      sourceToolId: 'service.recent_tool_calls_get',
      fieldName: 'calls',
      items: context.runtimeState.recentCalls(args.maxEntries),
      summary: {
        kind: 'toolCalls',
      },
      pageSize: clampNumber(Number(args.maxEntries) || 100, 1, 200),
    }),
  });

  register({
    id: 'service.token_regenerate',
    description: 'Regenerate the adapter token and return the new token value.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: {} }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'service.token_regenerate',
    danger: AiToolDanger.medium,
    requiresConfirmation: true,
    handler: async () => ({
      token: await context.regenerateToken(),
      acceptedTokenHeaders: context.config.acceptedTokenHeaders,
    }),
  });

  register({
    id: 'browser.chrome_status_get',
    description: 'Return Chrome remote debugging availability, version metadata, and target counts.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: {} }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.chrome_status_get',
    handler: async () => {
      try {
        const version = await getChromeVersion(context.config);
        const targets = await listTargets(context.config);
        return {
          reachable: true,
          version,
          targetCount: targets.length,
          currentTargetId: context.browserState.currentTargetId || null,
        };
      } catch (error) {
        return {
          reachable: false,
          error: error.message,
          currentTargetId: context.browserState.currentTargetId || null,
        };
      }
    },
  });

  register({
    id: 'browser.chrome_launch',
    description: 'Launch Chrome with remote debugging enabled for this adapter.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        url: { type: 'string' },
        headless: { type: 'boolean' },
        userDataDir: { type: 'string' },
        timeoutMs: { type: 'integer', minimum: 1000, maximum: 60000 },
        extraArgs: { type: 'array', items: { type: 'string' } },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.chrome_launch',
    danger: AiToolDanger.medium,
    handler: async (args) => {
      const launchInfo = await launchChrome(context.config, {
        url: args.url ? String(args.url) : '',
        headless: Boolean(args.headless),
        userDataDir: args.userDataDir ? resolveWithinRoot(context.config.rootDir, String(args.userDataDir)) : '',
        timeoutMs: args.timeoutMs,
        extraArgs: Array.isArray(args.extraArgs) ? args.extraArgs : [],
      });
      context.browserState.lastLaunchInfo = launchInfo;
      return launchInfo;
    },
  });

  register({
    id: 'browser.targets_list',
    description: 'List remote debugging targets known to Chrome.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        type: { type: 'string' },
        limit: { type: 'integer', minimum: 1, maximum: 200 },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.targets_list',
    handler: async (args) => {
      let targets = await listTargets(context.config);
      if (args.type) {
        targets = targets.filter((target) => target.type === String(args.type));
      }
      const normalized = targets.map(normalizeTarget);
      if (!context.browserState.currentTargetId && normalized.length > 0) {
        context.browserState.currentTargetId =
          normalized.find((target) => target.type === 'page')?.id ?? normalized[0].id;
      }
      return context.resultHandleStore.buildItemsResult({
        sourceToolId: 'browser.targets_list',
        fieldName: 'targets',
        items: normalized,
        summary: {
          type: args.type || '',
        },
        pageSize: clampNumber(Number(args.limit) || 20, 1, 200),
      });
    },
  });

  register({
    id: 'browser.current_target_get',
    description: 'Return the adapter current target, resolving a default page target if necessary.',
    argsSchemaJson: schemaJson({ type: 'object', additionalProperties: false, properties: {} }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.current_target_get',
    handler: async () => {
      const target = await resolveTarget(context, '');
      return {
        currentTargetId: context.browserState.currentTargetId,
        target: normalizeTarget(target),
      };
    },
  });

  register({
    id: 'browser.page_open',
    description: 'Open a new page target in Chrome and make it the current target.',
    argsSchemaJson: schemaJson({
      type: 'object',
      required: ['url'],
      additionalProperties: false,
      properties: {
        url: { type: 'string' },
        activate: { type: 'boolean' },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.page_open',
    handler: async (args) => {
      const created = await createTarget(context.config, String(args.url));
      context.browserState.currentTargetId = created.id;
      if (args.activate !== false) {
        await activateTarget(context.config, created.id);
      }
      return {
        target: normalizeTarget(created),
        currentTargetId: context.browserState.currentTargetId,
      };
    },
  });

  register({
    id: 'browser.page_activate',
    description: 'Activate an existing target and make it the current target.',
    argsSchemaJson: schemaJson({
      type: 'object',
      required: ['targetId'],
      additionalProperties: false,
      properties: {
        targetId: { type: 'string' },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.page_activate',
    handler: async (args) => {
      const targetId = String(args.targetId);
      const response = await activateTarget(context.config, targetId);
      context.browserState.currentTargetId = targetId;
      return {
        targetId,
        status: response.trim(),
      };
    },
  });

  register({
    id: 'browser.page_close',
    description: 'Close an existing target and clear it if it was the current target.',
    argsSchemaJson: schemaJson({
      type: 'object',
      required: ['targetId'],
      additionalProperties: false,
      properties: {
        targetId: { type: 'string' },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.page_close',
    danger: AiToolDanger.medium,
    handler: async (args) => {
      const targetId = String(args.targetId);
      const response = await closeTarget(context.config, targetId);
      if (context.browserState.currentTargetId === targetId) {
        context.browserState.currentTargetId = '';
      }
      return {
        targetId,
        status: response.trim(),
        currentTargetId: context.browserState.currentTargetId || null,
      };
    },
  });

  register({
    id: 'browser.page_navigate',
    description: 'Navigate a page target to a new URL through Chrome DevTools Protocol.',
    argsSchemaJson: schemaJson({
      type: 'object',
      required: ['url'],
      additionalProperties: false,
      properties: {
        url: { type: 'string' },
        targetId: { type: 'string' },
        waitForLoad: { type: 'boolean' },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.page_navigate',
    handler: async (args) => {
      const target = await resolveTarget(context, args.targetId ? String(args.targetId) : '');
      return withTargetSession(context, target, async (session) => {
        await session.send('Page.enable');
        const shouldWait = args.waitForLoad !== false;
        const waitForLoad = shouldWait ? session.waitForEvent('Page.loadEventFired', { timeoutMs: 15000 }) : null;
        const navigation = await session.send('Page.navigate', { url: String(args.url) });
        if (waitForLoad) {
          try {
            await waitForLoad;
          } catch {
            // Navigation may still succeed even if the load event is delayed.
          }
        }
        return {
          target: normalizeTarget(target),
          navigation,
          waitedForLoad: shouldWait,
        };
      });
    },
  });

  register({
    id: 'browser.page_reload',
    description: 'Reload a page target through Chrome DevTools Protocol.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        targetId: { type: 'string' },
        ignoreCache: { type: 'boolean' },
        waitForLoad: { type: 'boolean' },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.page_reload',
    handler: async (args) => {
      const target = await resolveTarget(context, args.targetId ? String(args.targetId) : '');
      return withTargetSession(context, target, async (session) => {
        await session.send('Page.enable');
        const shouldWait = args.waitForLoad !== false;
        const waitForLoad = shouldWait ? session.waitForEvent('Page.loadEventFired', { timeoutMs: 15000 }) : null;
        await session.send('Page.reload', { ignoreCache: Boolean(args.ignoreCache) });
        if (waitForLoad) {
          try {
            await waitForLoad;
          } catch {
            // ignore
          }
        }
        return {
          target: normalizeTarget(target),
          reloaded: true,
          waitedForLoad: shouldWait,
        };
      });
    },
  });

  register({
    id: 'browser.runtime_evaluate',
    description: 'Evaluate JavaScript in the selected page target and return the DevTools Protocol result.',
    argsSchemaJson: schemaJson({
      type: 'object',
      required: ['expression'],
      additionalProperties: false,
      properties: {
        expression: { type: 'string' },
        targetId: { type: 'string' },
        awaitPromise: { type: 'boolean' },
        returnByValue: { type: 'boolean' },
        includeCommandLineAPI: { type: 'boolean' },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.runtime_evaluate',
    handler: async (args) => {
      const target = await resolveTarget(context, args.targetId ? String(args.targetId) : '');
      return withTargetSession(context, target, async (session) => {
        await session.send('Runtime.enable');
        const evaluation = await session.send('Runtime.evaluate', {
          expression: String(args.expression),
          awaitPromise: args.awaitPromise !== false,
          returnByValue: args.returnByValue !== false,
          includeCommandLineAPI: Boolean(args.includeCommandLineAPI),
        });
        return {
          target: normalizeTarget(target),
          evaluation,
        };
      });
    },
  });

  register({
    id: 'browser.dom_document_get',
    description: 'Read the DOM document tree for the selected page target.',
    argsSchemaJson: schemaJson({
      type: 'object',
      additionalProperties: false,
      properties: {
        targetId: { type: 'string' },
        depth: { type: 'integer', minimum: -1, maximum: 16 },
        pierce: { type: 'boolean' },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.dom_document_get',
    handler: async (args) => {
      const target = await resolveTarget(context, args.targetId ? String(args.targetId) : '');
      return withTargetSession(context, target, async (session) => {
        await session.send('DOM.enable');
        const documentResult = await session.send('DOM.getDocument', {
          depth: Number.isInteger(args.depth) ? args.depth : 4,
          pierce: Boolean(args.pierce),
        });
        return context.resultHandleStore.buildTextResult({
          sourceToolId: 'browser.dom_document_get',
          text: JSON.stringify({
            target: normalizeTarget(target),
            document: documentResult.root,
          }, null, 2),
          summary: {
            targetId: target.id,
          },
        });
      });
    },
  });

  register({
    id: 'browser.dom_query_selector_all',
    description: 'Query the DOM by CSS selector and return matching node summaries.',
    argsSchemaJson: schemaJson({
      type: 'object',
      required: ['selector'],
      additionalProperties: false,
      properties: {
        selector: { type: 'string' },
        targetId: { type: 'string' },
        limit: { type: 'integer', minimum: 1, maximum: 100 },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.dom_query_selector_all',
    handler: async (args) => {
      const target = await resolveTarget(context, args.targetId ? String(args.targetId) : '');
      return withTargetSession(context, target, async (session) => {
        await session.send('DOM.enable');
        const documentResult = await session.send('DOM.getDocument', { depth: 1, pierce: true });
        const matches = await session.send('DOM.querySelectorAll', {
          nodeId: documentResult.root.nodeId,
          selector: String(args.selector),
        });
        const safeLimit = clampNumber(Number(args.limit) || 20, 1, 100);
        const items = [];
        for (const nodeId of (matches.nodeIds ?? []).slice(0, safeLimit)) {
          const node = await session.send('DOM.describeNode', { nodeId, depth: 0 });
          items.push(normalizeDomNode(node.node));
        }
        return context.resultHandleStore.buildItemsResult({
          sourceToolId: 'browser.dom_query_selector_all',
          fieldName: 'nodes',
          items,
          summary: {
            selector: String(args.selector),
            targetId: target.id,
            matchCount: (matches.nodeIds ?? []).length,
          },
          pageSize: safeLimit,
        });
      });
    },
  });

  register({
    id: 'browser.page_capture_screenshot',
    description: 'Capture a screenshot from the selected page target and write it to disk.',
    argsSchemaJson: schemaJson({
      type: 'object',
      required: ['outputPath'],
      additionalProperties: false,
      properties: {
        outputPath: { type: 'string' },
        targetId: { type: 'string' },
        format: { type: 'string', enum: ['png', 'jpeg', 'webp'] },
        quality: { type: 'integer', minimum: 0, maximum: 100 },
        fullPage: { type: 'boolean' },
        fromSurface: { type: 'boolean' },
      },
    }),
    returnSchemaJson: schemaJson({ type: 'object' }),
    handlerName: 'browser.page_capture_screenshot',
    danger: AiToolDanger.high,
    requiresConfirmation: true,
    handler: async (args) => {
      const target = await resolveTarget(context, args.targetId ? String(args.targetId) : '');
      const outputPath = resolveWithinRoot(context.config.rootDir, String(args.outputPath));
      return withTargetSession(context, target, async (session) => {
        await session.send('Page.enable');
        let metricsOverridden = false;
        const format = args.format ? String(args.format) : 'png';
        try {
          const screenshotArgs = {
            format,
            fromSurface: args.fromSurface !== false,
          };
          if (format === 'jpeg' && Number.isFinite(args.quality)) {
            screenshotArgs.quality = clampNumber(Number(args.quality), 0, 100);
          }
          if (args.fullPage) {
            const metrics = await session.send('Page.getLayoutMetrics');
            const contentSize = metrics.cssContentSize ?? metrics.contentSize ?? { width: 1280, height: 720 };
            const width = Math.max(1, Math.ceil(contentSize.width));
            const height = Math.max(1, Math.ceil(contentSize.height));
            await session.send('Emulation.setDeviceMetricsOverride', {
              mobile: false,
              width,
              height,
              deviceScaleFactor: 1,
            });
            metricsOverridden = true;
            screenshotArgs.clip = {
              x: 0,
              y: 0,
              width,
              height,
              scale: 1,
            };
          }
          const screenshot = await session.send('Page.captureScreenshot', screenshotArgs, { timeoutMs: 30000 });
          await fs.mkdir(path.dirname(outputPath), { recursive: true });
          await fs.writeFile(outputPath, Buffer.from(screenshot.data, 'base64'));
        } finally {
          if (metricsOverridden) {
            try {
              await session.send('Emulation.clearDeviceMetricsOverride');
            } catch {
              // ignore cleanup failures
            }
          }
        }
        const stat = await fs.stat(outputPath);
        return {
          target: normalizeTarget(target),
          outputPath,
          bytesWritten: stat.size,
          format,
          fullPage: Boolean(args.fullPage),
        };
      });
    },
  });
}

async function withTargetSession(context, target, callback) {
  const session = new ChromeSession(target.webSocketDebuggerUrl);
  await session.connect({ timeoutMs: 10000 });
  try {
    return await callback(session);
  } finally {
    await session.close();
  }
}

async function resolveTarget(context, requestedTargetId) {
  const targets = await listTargets(context.config);
  if (targets.length === 0) {
    throw new Error('Chrome has no available targets.');
  }
  const explicitTarget = requestedTargetId
    ? targets.find((item) => item.id === requestedTargetId)
    : null;
  let target = explicitTarget ?? null;
  if (!target && context.browserState.currentTargetId) {
    const currentTarget = targets.find((item) => item.id === context.browserState.currentTargetId);
    if (currentTarget?.type === 'page') {
      target = currentTarget;
    }
  }
  if (!target) {
    target = targets.find((item) => item.type === 'page')
      ?? targets.find((item) => item.id === context.browserState.currentTargetId)
      ?? targets[0];
  }
  context.browserState.currentTargetId = target.id;
  return target;
}

function normalizeTarget(target) {
  return {
    id: target.id,
    type: target.type,
    title: target.title ?? '',
    url: target.url ?? '',
    attached: Boolean(target.attached),
    webSocketDebuggerUrl: target.webSocketDebuggerUrl ?? '',
  };
}

function normalizeDomNode(node) {
  return {
    nodeId: node.nodeId,
    backendNodeId: node.backendNodeId,
    nodeType: node.nodeType,
    nodeName: node.nodeName,
    localName: node.localName,
    nodeValue: node.nodeValue,
    attributes: attributesToMap(node.attributes ?? []),
  };
}

function attributesToMap(attributes) {
  const map = {};
  for (let index = 0; index < attributes.length; index += 2) {
    map[attributes[index]] = attributes[index + 1];
  }
  return map;
}

function resolveWithinRoot(rootDir, value) {
  return path.isAbsolute(value) ? value : path.resolve(rootDir, value);
}
