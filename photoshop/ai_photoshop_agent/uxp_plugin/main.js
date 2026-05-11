const { app, core } = require("photoshop");
const { entrypoints, storage } = require("uxp");

const fs = storage.localFileSystem;
const BRIDGE_TOKEN_KEY = "ai_photoshop_agent_bridge_folder_token";
const processedRequests = new Map();
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
let panelRoot = null;
let bridgeFolder = null;
let pollTimer = null;

function setText(id, value) {
  if (!panelRoot) {
    return;
  }
  const node = panelRoot.querySelector(`#${id}`);
  if (node) {
    node.textContent = value;
  }
}

function setDebug(value) {
  if (!panelRoot) {
    return;
  }
  const node = panelRoot.querySelector("#debug");
  if (node) {
    node.textContent = value;
  }
}

async function ensureFolder(parent, name) {
  try {
    return await parent.getEntry(name);
  } catch (error) {
    return parent.createFolder(name);
  }
}

async function ensureFile(parent, name) {
  try {
    return await parent.getEntry(name);
  } catch (error) {
    return parent.createFile(name, { overwrite: true });
  }
}

async function writeJsonFile(parent, name, value) {
  const file = await ensureFile(parent, name);
  await file.write(JSON.stringify(value, null, 2));
}

async function readJsonFile(file) {
  return JSON.parse(await file.read());
}

async function readTextFile(file) {
  return file.read();
}

async function chooseBridgeFolder() {
  const folder = await fs.getFolder();
  if (!folder) {
    return;
  }
  bridgeFolder = folder;
  const token = await fs.createPersistentToken(folder);
  localStorage.setItem(BRIDGE_TOKEN_KEY, token);
  await refreshStatus();
}

async function restoreBridgeFolder() {
  const token = localStorage.getItem(BRIDGE_TOKEN_KEY);
  if (!token) {
    return null;
  }
  try {
    bridgeFolder = await fs.getEntryForPersistentToken(token);
    return bridgeFolder;
  } catch (error) {
    localStorage.removeItem(BRIDGE_TOKEN_KEY);
    bridgeFolder = null;
    return null;
  }
}

async function getGeneratedToolsFolder() {
  if (!bridgeFolder) {
    throw new Error("Bridge folder not configured.");
  }
  return ensureFolder(bridgeFolder, "generated_tools");
}

function summarizeDocument(document) {
  return {
    id: document.id,
    title: document.title,
    width: document.width,
    height: document.height,
    resolution: document.resolution,
    mode: document.mode,
    layerCount: document.layers.length
  };
}

function summarizeLayer(layer) {
  return {
    id: layer.id,
    name: layer.name,
    kind: layer.kind,
    visible: layer.visible,
    opacity: layer.opacity,
    blendMode: layer.blendMode,
    isGroup: Array.isArray(layer.layers) && layer.layers.length > 0,
    childCount: Array.isArray(layer.layers) ? layer.layers.length : 0,
    bounds: layer.bounds || null
  };
}

function collectLayers(layers, output, parentId = null) {
  for (const layer of layers) {
    output.push({
      ...summarizeLayer(layer),
      parentId
    });
    if (Array.isArray(layer.layers) && layer.layers.length > 0) {
      collectLayers(layer.layers, output, layer.id);
    }
  }
}

function findDocumentById(documentId) {
  if (!documentId) {
    return app.activeDocument;
  }
  const document = app.documents.find((candidate) => candidate.id === documentId);
  if (!document) {
    throw new Error(`Document not found: ${documentId}`);
  }
  return document;
}

function findLayerById(layers, layerId) {
  for (const layer of layers) {
    if (layer.id === layerId) {
      return layer;
    }
    if (Array.isArray(layer.layers) && layer.layers.length > 0) {
      const child = findLayerById(layer.layers, layerId);
      if (child) {
        return child;
      }
    }
  }
  return null;
}

function listLayers(documentId) {
  const document = findDocumentById(documentId);
  const layers = [];
  collectLayers(document.layers, layers);
  return layers;
}

async function loadGeneratedToolDefinitions() {
  const folder = await getGeneratedToolsFolder();
  const entries = await folder.getEntries();
  const definitions = [];
  for (const entry of entries) {
    if (!entry.isFile || !entry.name.endsWith(".json")) {
      continue;
    }
    definitions.push({ ...JSON.parse(await readTextFile(entry)), fileName: entry.name });
  }
  return definitions;
}

async function findGeneratedToolDefinition(toolId) {
  const definitions = await loadGeneratedToolDefinitions();
  return definitions.find((definition) => definition.toolId === toolId) || null;
}

function buildGeneratedToolHost() {
  return {
    app,
    core,
    bridgeFolder,
    requireActiveDocument(documentId) {
      return findDocumentById(documentId);
    },
    findDocumentById,
    findLayerById,
    listLayers,
    listDocuments() {
      return app.documents.map((document) => summarizeDocument(document));
    },
    summarizeDocument,
    summarizeLayer,
    collectLayers,
    async modal(commandName, callback) {
      return core.executeAsModal(callback, { commandName: String(commandName || "AI Platform Agent Generated Tool") });
    }
  };
}

async function executeGeneratedTool(definition, args) {
  const runner = new AsyncFunction("args", "host", "require", "console", definition.source);
  return runner(args, buildGeneratedToolHost(), require, console);
}

async function executeBridgeTool(toolId, args) {
  switch (toolId) {
    case "photoshop.documents_list":
      return app.documents.map((document) => summarizeDocument(document));
    case "photoshop.active_document_get":
      if (!app.activeDocument) {
        throw new Error("No active Photoshop document.");
      }
      return summarizeDocument(app.activeDocument);
    case "photoshop.layers_list": {
      const document = findDocumentById(args.documentId);
      const layers = [];
      collectLayers(document.layers, layers);
      return layers;
    }
    case "photoshop.layer_detail_get": {
      const document = findDocumentById(args.documentId);
      const layer = findLayerById(document.layers, args.layerId);
      if (!layer) {
        throw new Error(`Layer not found: ${args.layerId}`);
      }
      return {
        summary: summarizeLayer(layer),
        textItem: layer.textItem ? { contents: layer.textItem.contents } : null,
        bounds: layer.bounds || null
      };
    }
    case "photoshop.layer_visibility_set": {
      const document = findDocumentById(args.documentId);
      const layer = findLayerById(document.layers, args.layerId);
      if (!layer) {
        throw new Error(`Layer not found: ${args.layerId}`);
      }
      await core.executeAsModal(async () => {
        layer.visible = Boolean(args.visible);
      }, { commandName: "AI Platform Agent: Set Layer Visibility" });
      return { layerId: layer.id, visible: layer.visible };
    }
    case "photoshop.text_layer_set": {
      const document = findDocumentById(args.documentId);
      const layer = findLayerById(document.layers, args.layerId);
      if (!layer) {
        throw new Error(`Layer not found: ${args.layerId}`);
      }
      if (!layer.textItem) {
        throw new Error(`Layer is not a text layer: ${args.layerId}`);
      }
      await core.executeAsModal(async () => {
        layer.textItem.contents = String(args.contents ?? "");
      }, { commandName: "AI Platform Agent: Set Text Layer" });
      return { layerId: layer.id, contents: layer.textItem.contents };
    }
    case "photoshop.document_save": {
      const document = findDocumentById(args.documentId);
      await core.executeAsModal(async () => {
        await document.save();
      }, { commandName: "AI Platform Agent: Save Document" });
      return { documentId: document.id, title: document.title, saved: true };
    }
  }

  const generatedDefinition = await findGeneratedToolDefinition(toolId);
  if (generatedDefinition) {
    return executeGeneratedTool(generatedDefinition, args);
  }
  throw new Error(`Unsupported bridge tool: ${toolId}`);
}

async function processRequestFile(requestsFolder, responsesFolder, file) {
  if (!file.isFile) {
    return;
  }
  if (processedRequests.has(file.name)) {
    return;
  }
  const request = await readJsonFile(file);
  processedRequests.set(file.name, Date.now());
  try {
    const result = await executeBridgeTool(request.toolId, request.args || {});
    await writeJsonFile(responsesFolder, `${request.requestId}.json`, {
      ok: true,
      requestId: request.requestId,
      completedAt: new Date().toISOString(),
      result
    });
  } catch (error) {
    await writeJsonFile(responsesFolder, `${request.requestId}.json`, {
      ok: false,
      requestId: request.requestId,
      completedAt: new Date().toISOString(),
      error: String(error && error.stack ? error.stack : error)
    });
  }
}

async function publishHeartbeat(folder, lastError = "") {
  const requestsFolder = await ensureFolder(folder, "requests");
  const responsesFolder = await ensureFolder(folder, "responses");
  const generatedToolsFolder = await getGeneratedToolsFolder();
  await writeJsonFile(folder, "status.json", {
    pluginReady: true,
    lastHeartbeat: new Date().toISOString(),
    photoshopVersion: app.version,
    activeDocumentTitle: app.activeDocument ? app.activeDocument.title : "",
    openDocumentCount: app.documents.length,
    requestsPath: fs.getNativePath(requestsFolder),
    responsesPath: fs.getNativePath(responsesFolder),
    generatedToolsPath: fs.getNativePath(generatedToolsFolder),
    lastError
  });
}

async function tick() {
  if (!bridgeFolder) {
    setText("plugin-status", "Bridge folder not configured");
    return;
  }
  try {
    const requestsFolder = await ensureFolder(bridgeFolder, "requests");
    const responsesFolder = await ensureFolder(bridgeFolder, "responses");
    const entries = await requestsFolder.getEntries();
    for (const entry of entries) {
      await processRequestFile(requestsFolder, responsesFolder, entry);
    }
    for (const [name, timestamp] of Array.from(processedRequests.entries())) {
      if (Date.now() - timestamp > 15000) {
        processedRequests.delete(name);
      }
    }
    await publishHeartbeat(bridgeFolder);
    setText("plugin-status", "Running");
    setText("heartbeat", new Date().toISOString());
    setText("last-error", "None");
    setDebug(JSON.stringify({
      bridgePath: fs.getNativePath(bridgeFolder),
      openDocumentCount: app.documents.length,
      activeDocument: app.activeDocument ? app.activeDocument.title : null
    }, null, 2));
  } catch (error) {
    const text = String(error && error.stack ? error.stack : error);
    setText("plugin-status", "Error");
    setText("last-error", text);
    setDebug(text);
    if (bridgeFolder) {
      try {
        await publishHeartbeat(bridgeFolder, text);
      } catch (_publishError) {
        // Ignore secondary publish errors.
      }
    }
  }
}

async function refreshStatus() {
  if (!bridgeFolder) {
    setText("bridge-path", "Not configured");
    setText("plugin-status", "Idle");
    setText("heartbeat", "Unknown");
    return;
  }
  setText("bridge-path", fs.getNativePath(bridgeFolder));
  await tick();
}

function wireUi(rootNode) {
  panelRoot = rootNode;
  panelRoot.querySelector("#choose-bridge").addEventListener("click", () => {
    chooseBridgeFolder().catch((error) => {
      setText("last-error", String(error));
    });
  });
  panelRoot.querySelector("#refresh-status").addEventListener("click", () => {
    refreshStatus().catch((error) => {
      setText("last-error", String(error));
    });
  });
}

function startPolling() {
  if (pollTimer) {
    clearInterval(pollTimer);
  }
  pollTimer = setInterval(() => {
    tick().catch((error) => {
      setText("last-error", String(error));
    });
  }, 750);
}

entrypoints.setup({
  panels: {
    aiPhotoshopAgentPanel: {
      async show(rootNode) {
        wireUi(rootNode);
        await restoreBridgeFolder();
        await refreshStatus();
        startPolling();
      },
      hide() {
        if (pollTimer) {
          clearInterval(pollTimer);
          pollTimer = null;
        }
      }
    }
  }
});
