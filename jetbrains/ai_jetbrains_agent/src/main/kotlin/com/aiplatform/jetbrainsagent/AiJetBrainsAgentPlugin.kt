package com.aiplatform.jetbrainsagent

import com.google.gson.GsonBuilder
import com.google.gson.reflect.TypeToken
import com.intellij.execution.RunManager
import com.intellij.ide.plugins.PluginManagerCore
import com.intellij.openapi.Disposable
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.application.ReadAction
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.service
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.module.ModuleManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.project.ProjectManager
import com.intellij.openapi.startup.StartupActivity
import com.intellij.openapi.util.Disposer
import com.intellij.openapi.vfs.LocalFileSystem
import com.intellij.openapi.vfs.VfsUtilCore
import com.intellij.psi.PsiManager
import com.intellij.psi.util.PsiTreeUtil
import com.sun.net.httpserver.HttpExchange
import com.sun.net.httpserver.HttpServer
import java.io.InputStreamReader
import java.net.InetSocketAddress
import java.nio.charset.StandardCharsets
import java.nio.file.Path
import java.nio.file.Paths
import java.security.MessageDigest
import java.time.Instant
import java.util.concurrent.Executors
import kotlin.io.path.createDirectories
import kotlin.io.path.exists
import kotlin.io.path.readText
import kotlin.io.path.writeText

private val gson = GsonBuilder().setPrettyPrinting().create()
private val objectMapType = object : TypeToken<MutableMap<String, Any?>>() {}.type

data class AiJetBrainsAgentConfig(
    val rootDir: Path,
    val host: String = "127.0.0.1",
    val port: Int = 19791,
    val requireToken: Boolean = true,
    val fullAccessEnabled: Boolean = false,
    val toolTimeoutMs: Long = 120_000,
    val serviceVersion: String = "0.1.0",
) {
    companion object {
        const val FRAMEWORK_NAME = "AI Platform Agent Framework"
        const val PROTOCOL_VERSION = "2.0"
        const val SERVICE_ID = "aijetbrains.agent"
        const val SERVICE_NAME = "AI JetBrains Agent"
        const val PLATFORM_ID = "jetbrains"
        const val PRIMARY_TOKEN_HEADER = "X-AI-Agent-Token"
        const val LEGACY_TOKEN_HEADER = "X-Jetbrains-Ai-Token"
    }

    val acceptedTokenHeaders: List<String>
        get() = listOf(PRIMARY_TOKEN_HEADER, LEGACY_TOKEN_HEADER)

    val serverUrl: String
        get() = "http://$host:$port"

    val stateDirectoryPath: Path
        get() = rootDir.resolve(".ai_platform_agent").resolve("jetbrains")

    val tokenFilePath: Path
        get() = stateDirectoryPath.resolve("token.txt")

    val generatedToolsDirectoryPath: Path
        get() = stateDirectoryPath.resolve("generated_tools")
}

class AiRuntimeState {
    private val serviceLogs = ArrayDeque<Map<String, Any?>>()
    private val toolCalls = ArrayDeque<Map<String, Any?>>()

    fun log(level: String, message: String) {
        serviceLogs.addLast(mapOf("time" to isoTimestamp(), "level" to level, "message" to message))
        while (serviceLogs.size > 400) serviceLogs.removeFirst()
    }

    fun recordCall(toolId: String, ok: Boolean, durationMs: Long, message: String) {
        toolCalls.addLast(mapOf("time" to isoTimestamp(), "toolId" to toolId, "ok" to ok, "durationMs" to durationMs, "message" to message))
        while (toolCalls.size > 400) toolCalls.removeFirst()
    }

    fun recentLogs(maxEntries: Int): List<Map<String, Any?>> = serviceLogs.takeLast(clampNumber(maxEntries, 1, 300))
    fun recentCalls(maxEntries: Int): List<Map<String, Any?>> = toolCalls.takeLast(clampNumber(maxEntries, 1, 300))
}

data class AiManifestBundleDefinition(val id: String, val description: String, val prefixes: List<String>)

data class AiToolDefinition(
    val id: String,
    val description: String,
    val argsSchemaJson: String,
    val returnSchemaJson: String,
    val handlerName: String,
    val danger: String = "low",
    val requiresConfirmation: Boolean = false,
    val handler: (MutableMap<String, Any?>, AiToolExecutionContext) -> Any?,
) {
    val namespaceId: String = id.substringBefore('.')

    fun toSummaryJson(): Map<String, Any?> = mapOf(
        "id" to id,
        "namespaceId" to namespaceId,
        "description" to description,
        "danger" to danger,
        "requiresConfirmation" to requiresConfirmation,
    )

    fun toFullJson(): Map<String, Any?> = mapOf(
        "id" to id,
        "namespaceId" to namespaceId,
        "description" to description,
        "argsSchemaJson" to argsSchemaJson,
        "returnSchemaJson" to returnSchemaJson,
        "danger" to danger,
        "requiresConfirmation" to requiresConfirmation,
        "handlerName" to handlerName,
    )
}

data class AiToolExecutionContext(
    val config: AiJetBrainsAgentConfig,
    var registry: AiToolRegistry,
    val resultHandleStore: ResultHandleStore,
    val runtimeState: AiRuntimeState,
    val agentManual: String,
    val readToken: () -> String,
    val regenerateToken: () -> String,
    val buildHealthPayload: (Boolean) -> Map<String, Any?>,
    val buildAgentBriefPayload: (Boolean) -> Map<String, Any?>,
    val generatedToolStore: JetBrainsGeneratedToolStore,
    val reloadGeneratedTools: () -> Map<String, Any?>,
)

class AiToolRegistry(private val bundles: List<AiManifestBundleDefinition>) {
    private val tools = linkedMapOf<String, AiToolDefinition>()

    fun register(tool: AiToolDefinition) {
        check(!tools.containsKey(tool.id)) { "Duplicate tool id: ${tool.id}" }
        tools[tool.id] = tool
    }

    fun findTool(id: String): AiToolDefinition? = tools[id]
    fun reset() = tools.clear()
    fun count(): Int = tools.size
    fun sortedTools(): List<AiToolDefinition> = tools.values.sortedBy { it.id }

    fun namespaceInfos(): List<Map<String, Any?>> = sortedTools()
        .groupingBy { it.namespaceId }
        .eachCount()
        .toSortedMap()
        .map { mapOf("id" to it.key, "count" to it.value) }

    fun manifestHash(): String {
        val digest = MessageDigest.getInstance("SHA-256")
        digest.update(AiJetBrainsAgentConfig.PROTOCOL_VERSION.toByteArray(StandardCharsets.UTF_8))
        for (tool in sortedTools()) {
            listOf(tool.id, tool.namespaceId, tool.description, tool.argsSchemaJson, tool.returnSchemaJson, tool.danger, tool.handlerName).forEach {
                digest.update(it.toByteArray(StandardCharsets.UTF_8))
            }
            digest.update(if (tool.requiresConfirmation) byteArrayOf(1) else byteArrayOf(0))
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    fun buildManifestSummary(config: AiJetBrainsAgentConfig): Map<String, Any?> = mapOf(
        "ok" to true,
        "framework" to AiJetBrainsAgentConfig.FRAMEWORK_NAME,
        "serviceId" to AiJetBrainsAgentConfig.SERVICE_ID,
        "service" to AiJetBrainsAgentConfig.SERVICE_NAME,
        "platformId" to AiJetBrainsAgentConfig.PLATFORM_ID,
        "version" to config.serviceVersion,
        "protocolVersion" to AiJetBrainsAgentConfig.PROTOCOL_VERSION,
        "manifestHash" to manifestHash(),
        "toolCount" to count(),
        "namespaces" to namespaceInfos(),
        "tools" to sortedTools().map { it.toSummaryJson() },
    )

    fun buildManifestFull(config: AiJetBrainsAgentConfig): Map<String, Any?> =
        buildManifestSummary(config).toMutableMap().apply { put("tools", sortedTools().map { it.toFullJson() }) }

    fun buildBundleIndex(): Map<String, Any?> = mapOf(
        "ok" to true,
        "platformId" to AiJetBrainsAgentConfig.PLATFORM_ID,
        "manifestHash" to manifestHash(),
        "bundles" to bundles.map { bundle ->
            mapOf(
                "id" to bundle.id,
                "description" to bundle.description,
                "toolCount" to sortedTools().count { tool -> bundle.prefixes.any { prefix -> tool.id.startsWith(prefix) } },
            )
        },
    )

    fun tryBuildBundle(bundleId: String): Map<String, Any?>? {
        val bundle = bundles.firstOrNull { it.id == bundleId } ?: return null
        return mapOf(
            "ok" to true,
            "platformId" to AiJetBrainsAgentConfig.PLATFORM_ID,
            "manifestHash" to manifestHash(),
            "bundle" to mapOf("id" to bundle.id, "description" to bundle.description),
            "tools" to sortedTools().filter { tool -> bundle.prefixes.any { prefix -> tool.id.startsWith(prefix) } }.map { it.toSummaryJson() },
        )
    }

    fun buildManifestSearch(query: String, limit: Int, namespaceId: String, bundleId: String): Map<String, Any?> {
        val safeLimit = clampNumber(if (limit == 0) 8 else limit, 1, 64)
        val tokens = query.lowercase().split(Regex("\\s+")).filter { it.isNotBlank() }
        val filteredByBundle = bundles.firstOrNull { it.id == bundleId }
        val tools = sortedTools().filter { tool ->
            (namespaceId.isBlank() || tool.namespaceId == namespaceId) &&
                (filteredByBundle == null || filteredByBundle.prefixes.any { prefix -> tool.id.startsWith(prefix) })
        }.map { tool -> tool to searchScore(tool, tokens) }
            .filter { tokens.isEmpty() || it.second > 0 }
            .sortedWith(compareByDescending<Pair<AiToolDefinition, Int>> { it.second }.thenBy { it.first.id })
            .take(safeLimit)
            .map { it.first.toSummaryJson() }

        return mapOf(
            "ok" to true,
            "platformId" to AiJetBrainsAgentConfig.PLATFORM_ID,
            "manifestHash" to manifestHash(),
            "query" to query,
            "namespaceId" to namespaceId,
            "bundleId" to bundleId,
            "returned" to tools.size,
            "tools" to tools,
        )
    }

    fun buildDescribeMany(ids: List<String>): Map<String, Any?> {
        val found = mutableListOf<Map<String, Any?>>()
        val missing = mutableListOf<String>()
        for (id in ids) {
            val tool = findTool(id)
            if (tool == null) missing.add(id) else found.add(tool.toFullJson())
        }
        return mapOf(
            "ok" to true,
            "platformId" to AiJetBrainsAgentConfig.PLATFORM_ID,
            "manifestHash" to manifestHash(),
            "returned" to found.size,
            "missing" to missing,
            "tools" to found,
        )
    }
}

class ResultHandleStore(private val maxHandles: Int = 96) {
    private data class Entry(
        val id: String,
        val kind: String,
        val sourceToolId: String,
        val fieldName: String,
        val createdAt: String,
        val summary: Map<String, Any?>,
        val items: List<Any?> = emptyList(),
        val text: String = "",
    )

    private val entries = linkedMapOf<String, Entry>()

    fun buildItemsResult(sourceToolId: String, fieldName: String, items: List<Any?>, summary: Map<String, Any?>, pageSize: Int): Map<String, Any?> {
        val safePageSize = clampNumber(pageSize, 1, 200)
        val returned = minOf(safePageSize, items.size)
        val hasMore = returned < items.size
        val response = mutableMapOf<String, Any?>(
            "summary" to summary,
            "returned" to returned,
            "pageSize" to safePageSize,
            "total" to items.size,
            "hasMore" to hasMore,
            fieldName to items.take(returned),
        )
        if (hasMore) {
            response["resultHandle"] = add(Entry(newHandleId(), "items", sourceToolId, fieldName, isoTimestamp(), summary, items))
        }
        return response
    }

    fun buildTextResult(sourceToolId: String, text: String, summary: Map<String, Any?>, length: Int): Map<String, Any?> {
        val safeLength = clampNumber(length, 1, 32768)
        val count = minOf(safeLength, text.length)
        val hasMore = count < text.length
        val response = mutableMapOf<String, Any?>(
            "summary" to summary,
            "offset" to 0,
            "limit" to safeLength,
            "count" to count,
            "totalChars" to text.length,
            "hasMore" to hasMore,
            "content" to text.take(count),
        )
        if (hasMore) {
            response["resultHandle"] = add(Entry(newHandleId(), "text", sourceToolId, "content", isoTimestamp(), summary, text = text))
        }
        return response
    }

    fun buildPage(handleId: String, offset: Int, limit: Int): Map<String, Any?>? {
        val entry = entries[handleId] ?: return null
        return if (entry.kind == "text") {
            val safeOffset = clampNumber(offset, 0, entry.text.length)
            val safeLimit = clampNumber(if (limit == 0) 4096 else limit, 1, 32768)
            val count = minOf(safeLimit, entry.text.length - safeOffset)
            mapOf(
                "ok" to true,
                "handleId" to entry.id,
                "kind" to "text",
                "sourceToolId" to entry.sourceToolId,
                "createdAt" to entry.createdAt,
                "offset" to safeOffset,
                "limit" to safeLimit,
                "count" to count,
                "totalChars" to entry.text.length,
                "hasMore" to safeOffset + count < entry.text.length,
                "summary" to entry.summary,
                "content" to entry.text.substring(safeOffset, safeOffset + count),
            )
        } else {
            val safeOffset = clampNumber(offset, 0, entry.items.size)
            val safeLimit = clampNumber(if (limit == 0) 20 else limit, 1, 200)
            val count = minOf(safeLimit, entry.items.size - safeOffset)
            mapOf(
                "ok" to true,
                "handleId" to entry.id,
                "kind" to "items",
                "sourceToolId" to entry.sourceToolId,
                "fieldName" to entry.fieldName,
                "createdAt" to entry.createdAt,
                "offset" to safeOffset,
                "limit" to safeLimit,
                "count" to count,
                "total" to entry.items.size,
                "hasMore" to safeOffset + count < entry.items.size,
                "summary" to entry.summary,
                entry.fieldName to entry.items.subList(safeOffset, safeOffset + count),
            )
        }
    }

    private fun add(entry: Entry): String {
        entries[entry.id] = entry
        while (entries.size > maxHandles) {
            val oldest = entries.keys.firstOrNull() ?: break
            entries.remove(oldest)
        }
        return entry.id
    }

    private fun newHandleId(): String = "${System.currentTimeMillis().toString(16)}${java.util.UUID.randomUUID().toString().replace("-", "").take(8)}"
}

@Service(Service.Level.APP)
class AiJetBrainsAgentAppService : Disposable {
    private val rootDir = Paths.get(com.intellij.openapi.application.PathManager.getSystemPath()).resolve("ai_platform_agent_host")
    private val config = AiJetBrainsAgentConfig(rootDir = rootDir)
    internal val runtimeState = AiRuntimeState()
    private val resultHandleStore = ResultHandleStore()
    internal val registry = AiToolRegistry(defaultBundles())
    internal val generatedToolStore = JetBrainsGeneratedToolStore(config, runtimeState)
    private val agentManual = runCatching {
        val plugin = PluginManagerCore.getPlugin(com.intellij.openapi.extensions.PluginId.getId("com.aiplatform.jetbrains.agent"))
        plugin?.pluginPath?.resolve("AGENT.md")?.takeIf { it.exists() }?.readText() ?: ""
    }.getOrDefault("")
    internal val toolContext = AiToolExecutionContext(
        config = config,
        registry = registry,
        resultHandleStore = resultHandleStore,
        runtimeState = runtimeState,
        agentManual = agentManual,
        readToken = { token },
        regenerateToken = { regenerateToken() },
        buildHealthPayload = { includeOk -> buildHealthPayload(includeOk) },
        buildAgentBriefPayload = { includeOk -> buildAgentBriefPayload(includeOk) },
        generatedToolStore = generatedToolStore,
        reloadGeneratedTools = { refreshRegistry(force = true) },
    )
    private var server: HttpServer? = null
    internal var generatedToolsFingerprint: String = ""
    private var token: String = ""

    fun ensureStarted() {
        if (server != null) return
        refreshRegistry(force = true)
        if (config.requireToken) {
            token = ensureToken()
        }
        server = HttpServer.create(InetSocketAddress(config.host, config.port), 0).apply {
            executor = Executors.newCachedThreadPool()
            createContext("/") { exchange -> handleRequest(exchange) }
            start()
        }
        runtimeState.log("info", "Service started at ${config.serverUrl}")
    }

    override fun dispose() {
        server?.stop(0)
        server = null
    }

    private fun handleRequest(exchange: HttpExchange) {
        addCommonHeaders(exchange)
        if (exchange.requestMethod == "OPTIONS") {
            exchange.sendResponseHeaders(204, -1)
            exchange.close()
            return
        }
        try {
            refreshRegistry()
            val pathName = exchange.requestURI.path.trimStart('/')
            if (pathName == "health" && exchange.requestMethod == "GET") {
                sendJson(exchange, 200, buildHealthPayload(true))
                return
            }
            if (!isAuthorized(exchange)) {
                sendJson(exchange, 401, mapOf("ok" to false, "error" to "Unauthorized. Provide ${AiJetBrainsAgentConfig.PRIMARY_TOKEN_HEADER} or ${AiJetBrainsAgentConfig.LEGACY_TOKEN_HEADER}."))
                return
            }
            when {
                (pathName == "manifest" || pathName == "manifest/summary") && exchange.requestMethod == "GET" -> sendJson(exchange, 200, registry.buildManifestSummary(config))
                pathName == "manifest/full" && exchange.requestMethod == "GET" -> sendJson(exchange, 200, registry.buildManifestFull(config))
                pathName == "manifest/bundles" && exchange.requestMethod == "GET" -> sendJson(exchange, 200, registry.buildBundleIndex())
                pathName.startsWith("manifest/bundle/") && exchange.requestMethod == "GET" -> {
                    val payload = registry.tryBuildBundle(pathName.removePrefix("manifest/bundle/"))
                    if (payload == null) sendJson(exchange, 404, mapOf("ok" to false, "error" to "Unknown manifest bundle.")) else sendJson(exchange, 200, payload)
                }
                pathName == "manifest/search" && exchange.requestMethod == "POST" -> {
                    val body = readBody(exchange)
                    sendJson(exchange, 200, registry.buildManifestSearch(body["query"]?.toString() ?: "", (body["limit"] as? Number)?.toInt() ?: 0, body["namespaceId"]?.toString() ?: "", body["bundleId"]?.toString() ?: ""))
                }
                pathName == "tool/describe_many" && exchange.requestMethod == "POST" -> {
                    val body = readBody(exchange)
                    val ids = (body["ids"] as? List<*>)?.map { it.toString() } ?: emptyList()
                    sendJson(exchange, 200, registry.buildDescribeMany(ids))
                }
                pathName == "agent/brief" && exchange.requestMethod == "GET" -> sendJson(exchange, 200, buildAgentBriefPayload(true))
                pathName == "agent" && exchange.requestMethod == "GET" -> sendJson(exchange, 200, mapOf("ok" to true, "content" to agentManual))
                pathName.startsWith("result/") && exchange.requestMethod == "GET" -> {
                    val handleId = pathName.removePrefix("result/")
                    val params = parseQuery(exchange.requestURI.rawQuery.orEmpty())
                    val payload = resultHandleStore.buildPage(handleId, params["offset"]?.toIntOrNull() ?: 0, params["limit"]?.toIntOrNull() ?: 0)
                    if (payload == null) sendJson(exchange, 404, mapOf("ok" to false, "error" to "Unknown result handle: $handleId")) else sendJson(exchange, 200, payload)
                }
                pathName.startsWith("call/") && exchange.requestMethod == "POST" -> {
                    val toolId = pathName.removePrefix("call/")
                    val tool = registry.findTool(toolId)
                    if (tool == null) {
                        sendJson(exchange, 404, mapOf("ok" to false, "toolId" to toolId, "error" to "Unknown tool: $toolId"))
                        return
                    }
                    if (tool.requiresConfirmation && !config.fullAccessEnabled) {
                        sendJson(exchange, 403, mapOf("ok" to false, "toolId" to toolId, "error" to "High-risk tool requires full access."))
                        return
                    }
                    val body = readBody(exchange)
                    val startedAt = System.currentTimeMillis()
                    try {
                        val result = tool.handler(body, toolContext)
                        runtimeState.recordCall(toolId, true, System.currentTimeMillis() - startedAt, "ok")
                        sendJson(exchange, 200, mapOf("ok" to true, "toolId" to toolId, "durationMs" to (System.currentTimeMillis() - startedAt), "result" to result))
                    } catch (t: Throwable) {
                        runtimeState.recordCall(toolId, false, System.currentTimeMillis() - startedAt, t.message ?: t.javaClass.name)
                        sendJson(exchange, 500, mapOf("ok" to false, "toolId" to toolId, "durationMs" to (System.currentTimeMillis() - startedAt), "error" to (t.message ?: t.javaClass.name)))
                    }
                }
                else -> sendJson(exchange, 404, mapOf("ok" to false, "error" to "Not found: $pathName"))
            }
        } catch (t: Throwable) {
            runtimeState.log("error", t.message ?: t.javaClass.name)
            sendJson(exchange, 500, mapOf("ok" to false, "error" to (t.message ?: t.javaClass.name)))
        }
    }

    fun buildHealthPayload(includeOk: Boolean): Map<String, Any?> = linkedMapOf<String, Any?>(
        "framework" to AiJetBrainsAgentConfig.FRAMEWORK_NAME,
        "service" to AiJetBrainsAgentConfig.SERVICE_NAME,
        "serviceId" to AiJetBrainsAgentConfig.SERVICE_ID,
        "version" to config.serviceVersion,
        "platformId" to AiJetBrainsAgentConfig.PLATFORM_ID,
        "protocolVersion" to AiJetBrainsAgentConfig.PROTOCOL_VERSION,
        "serverRunning" to (server != null),
        "requiresToken" to config.requireToken,
        "acceptedTokenHeaders" to config.acceptedTokenHeaders,
        "serverUrl" to config.serverUrl,
        "manifestHash" to registry.manifestHash(),
        "toolCount" to registry.count(),
        "namespaces" to registry.namespaceInfos(),
        "supportsManifestSearch" to true,
        "supportsDescribeMany" to true,
        "supportsResultHandles" to true,
        "supportsBundles" to true,
        "supportsTextChunking" to true,
        "supportsDynamicToolRegistration" to true,
        "recommendedFlow" to listOf(
            "GET /health and compare manifestHash before refreshing capabilities.",
            "Use POST /manifest/search or GET /manifest/bundle/{id} to narrow candidate tools.",
            "Use POST /tool/describe_many for exact argument and return schemas before calling tools.",
            "Use GET /result/{handleId} for additional pages or text chunks when a tool returns a resultHandle.",
            "Use GET /manifest/full only as a fallback when search is insufficient.",
        ),
        "paths" to mapOf(
            "health" to "/health",
            "manifestSummary" to "/manifest",
            "manifestFull" to "/manifest/full",
            "manifestSearch" to "/manifest/search",
            "manifestBundles" to "/manifest/bundles",
            "toolDescribeMany" to "/tool/describe_many",
            "call" to "/call/{toolId}",
            "agent" to "/agent",
            "agentBrief" to "/agent/brief",
            "resultPage" to "/result/{handleId}",
        ),
        "platform" to mapOf(
            "openProjectCount" to ProjectManager.getInstance().openProjects.size,
            "generatedToolsDirectoryPath" to config.generatedToolsDirectoryPath.toString(),
        ),
    ).apply { if (includeOk) put("ok", true) }

    fun buildAgentBriefPayload(includeOk: Boolean): Map<String, Any?> = linkedMapOf<String, Any?>(
        "framework" to AiJetBrainsAgentConfig.FRAMEWORK_NAME,
        "platformId" to AiJetBrainsAgentConfig.PLATFORM_ID,
        "summary" to "Use JetBrains project model, VFS, PSI, and run configuration tools through the shared discovery-first protocol instead of inferring IDE state from files alone.",
        "steps" to listOf(
            "Call GET /health and reuse cached capabilities while manifestHash stays unchanged.",
            "Search tools with POST /manifest/search or load a focused bundle with GET /manifest/bundle/{id}.",
            "Inspect projects, modules, files, and PSI before executing any future mutation tools.",
            "Request exact tool schemas with POST /tool/describe_many before calling unfamiliar tools.",
        ),
        "paths" to mapOf(
            "health" to "/health",
            "manifestSummary" to "/manifest",
            "manifestFull" to "/manifest/full",
            "manifestSearch" to "/manifest/search",
            "manifestBundles" to "/manifest/bundles",
            "toolDescribeMany" to "/tool/describe_many",
            "call" to "/call/{toolId}",
            "agent" to "/agent",
            "agentBrief" to "/agent/brief",
            "resultPage" to "/result/{handleId}",
        ),
    ).apply { if (includeOk) put("ok", true) }

    private fun addCommonHeaders(exchange: HttpExchange) {
        exchange.responseHeaders.add("Access-Control-Allow-Origin", "*")
        exchange.responseHeaders.add("Access-Control-Allow-Headers", "Content-Type, X-AI-Agent-Token, X-Jetbrains-Ai-Token")
        exchange.responseHeaders.add("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        exchange.responseHeaders.add("Content-Type", "application/json; charset=utf-8")
    }

    private fun sendJson(exchange: HttpExchange, statusCode: Int, payload: Map<String, Any?>) {
        val body = gson.toJson(payload).toByteArray(StandardCharsets.UTF_8)
        exchange.sendResponseHeaders(statusCode, body.size.toLong())
        exchange.responseBody.use { it.write(body) }
    }

    private fun readBody(exchange: HttpExchange): MutableMap<String, Any?> {
        return InputStreamReader(exchange.requestBody, StandardCharsets.UTF_8).use { reader ->
            gson.fromJson(reader, objectMapType) ?: mutableMapOf<String, Any?>()
        }
    }

    private fun parseQuery(query: String): Map<String, String> =
        query.split("&").filter { it.contains("=") }.associate {
            val (key, value) = it.split("=", limit = 2)
            key to java.net.URLDecoder.decode(value, StandardCharsets.UTF_8)
        }

    private fun isAuthorized(exchange: HttpExchange): Boolean {
        if (!config.requireToken) return true
        val provided = exchange.requestHeaders.getFirst(AiJetBrainsAgentConfig.PRIMARY_TOKEN_HEADER)
            ?: exchange.requestHeaders.getFirst(AiJetBrainsAgentConfig.LEGACY_TOKEN_HEADER)
        return !provided.isNullOrBlank() && provided == token
    }

    private fun ensureToken(): String {
        config.stateDirectoryPath.createDirectories()
        if (config.tokenFilePath.exists()) {
            val existing = config.tokenFilePath.readText().trim()
            if (existing.isNotBlank()) return existing
        }
        return regenerateToken()
    }

    private fun regenerateToken(): String {
        val value = java.util.UUID.randomUUID().toString().replace("-", "") + java.util.UUID.randomUUID().toString().replace("-", "").take(16)
        config.stateDirectoryPath.createDirectories()
        config.tokenFilePath.writeText(value)
        token = value
        runtimeState.log("info", "Token regenerated.")
        return value
    }
}

class AiJetBrainsAgentStartupActivity : StartupActivity.DumbAware {
    override fun runActivity(project: Project) {
        ApplicationManager.getApplication().service<AiJetBrainsAgentAppService>().ensureStarted()
    }
}

private fun defaultBundles(): List<AiManifestBundleDefinition> = listOf(
    AiManifestBundleDefinition("service", "Adapter health, logs, config, and recent calls.", listOf("service.")),
    AiManifestBundleDefinition("jetbrains.workspace", "Project, module, file, and document inspection tools.", listOf("jetbrains.workspace_", "jetbrains.projects_", "jetbrains.modules_", "jetbrains.files_", "jetbrains.document_")),
    AiManifestBundleDefinition("jetbrains.psi", "PSI inspection tools.", listOf("jetbrains.psi_")),
    AiManifestBundleDefinition("jetbrains.run", "Run configuration inspection tools.", listOf("jetbrains.run_configurations_")),
    AiManifestBundleDefinition("tooling.dynamic", "Generated tool management and dynamic registration tools.", listOf("tool.")),
)

private fun registerDefaultTools(registry: AiToolRegistry, context: AiToolExecutionContext) {
    fun register(tool: AiToolDefinition) = registry.register(tool)

    register(
        AiToolDefinition("service.health_get", "Return the adapter health payload.", schemaJson(mapOf("type" to "object", "additionalProperties" to false, "properties" to emptyMap<String, Any>())), schemaJson(mapOf("type" to "object")), "service.health_get") { _, ctx ->
            ctx.buildHealthPayload(true)
        },
    )
    register(
        AiToolDefinition("service.agent_brief_get", "Return the concise operating brief for this adapter.", schemaJson(mapOf("type" to "object", "additionalProperties" to false, "properties" to emptyMap<String, Any>())), schemaJson(mapOf("type" to "object")), "service.agent_brief_get") { _, ctx ->
            ctx.buildAgentBriefPayload(true)
        },
    )
    register(
        AiToolDefinition("service.config_get", "Return effective adapter configuration.", schemaJson(mapOf("type" to "object", "additionalProperties" to false, "properties" to emptyMap<String, Any>())), schemaJson(mapOf("type" to "object")), "service.config_get") { _, ctx ->
            mapOf(
                "rootDir" to ctx.config.rootDir.toString(),
                "requireToken" to ctx.config.requireToken,
                "fullAccessEnabled" to ctx.config.fullAccessEnabled,
                "acceptedTokenHeaders" to ctx.config.acceptedTokenHeaders,
                "supportsDynamicToolRegistration" to true,
                "generatedToolsDirectoryPath" to ctx.config.generatedToolsDirectoryPath.toString(),
            )
        },
    )
    register(
        AiToolDefinition("service.logs_get", "List recent service logs.", schemaJson(mapOf("type" to "object", "additionalProperties" to false, "properties" to mapOf("maxEntries" to mapOf("type" to "integer", "minimum" to 1, "maximum" to 300)))), schemaJson(mapOf("type" to "object")), "service.logs_get") { args, ctx ->
            ctx.resultHandleStore.buildItemsResult("service.logs_get", "logs", ctx.runtimeState.recentLogs((args["maxEntries"] as? Number)?.toInt() ?: 100), mapOf("kind" to "logs"), clampNumber((args["maxEntries"] as? Number)?.toInt() ?: 100, 1, 200))
        },
    )
    register(
        AiToolDefinition("service.recent_tool_calls_get", "List recent tool call outcomes recorded by the adapter.", schemaJson(mapOf("type" to "object", "additionalProperties" to false, "properties" to mapOf("maxEntries" to mapOf("type" to "integer", "minimum" to 1, "maximum" to 300)))), schemaJson(mapOf("type" to "object")), "service.recent_tool_calls_get") { args, ctx ->
            ctx.resultHandleStore.buildItemsResult("service.recent_tool_calls_get", "calls", ctx.runtimeState.recentCalls((args["maxEntries"] as? Number)?.toInt() ?: 100), mapOf("kind" to "toolCalls"), clampNumber((args["maxEntries"] as? Number)?.toInt() ?: 100, 1, 200))
        },
    )
    register(
        AiToolDefinition("service.token_regenerate", "Regenerate the adapter token and return the new token value.", schemaJson(mapOf("type" to "object", "additionalProperties" to false, "properties" to emptyMap<String, Any>())), schemaJson(mapOf("type" to "object")), "service.token_regenerate", danger = "medium", requiresConfirmation = true) { _, ctx ->
            mapOf("token" to ctx.regenerateToken(), "acceptedTokenHeaders" to ctx.config.acceptedTokenHeaders)
        },
    )
    register(
        AiToolDefinition("jetbrains.workspace_summary_get", "Return summary information for currently open projects and editors.", schemaJson(mapOf("type" to "object", "additionalProperties" to false, "properties" to emptyMap<String, Any>())), schemaJson(mapOf("type" to "object")), "jetbrains.workspace_summary_get") { _, _ ->
            readAction<List<Map<String, Any?>>>() {
                ProjectManager.getInstance().openProjects.map { project ->
                    val selectedEditor = FileEditorManager.getInstance(project).selectedTextEditor
                    mapOf(
                        "name" to project.name,
                        "basePath" to project.basePath,
                        "selectedEditorPath" to selectedEditor?.let { editor -> FileDocumentManager.getInstance().getFile(editor.document)?.path },
                        "moduleCount" to ModuleManager.getInstance(project).modules.size,
                    )
                }
            }
        },
    )
    register(
        AiToolDefinition("jetbrains.projects_list", "List currently open projects.", schemaJson(mapOf("type" to "object", "additionalProperties" to false, "properties" to mapOf("pageSize" to mapOf("type" to "integer", "minimum" to 1, "maximum" to 200)))), schemaJson(mapOf("type" to "object")), "jetbrains.projects_list") { args, ctx ->
            val items = readAction<List<Map<String, Any?>>>() {
                ProjectManager.getInstance().openProjects.map { project ->
                    mapOf("name" to project.name, "basePath" to project.basePath)
                }
            }
            ctx.resultHandleStore.buildItemsResult("jetbrains.projects_list", "projects", items, mapOf("kind" to "projects"), clampNumber((args["pageSize"] as? Number)?.toInt() ?: 100, 1, 200))
        },
    )
    register(
        AiToolDefinition("jetbrains.modules_list", "List modules for one open project.", schemaJson(mapOf("type" to "object", "additionalProperties" to false, "properties" to mapOf("projectName" to mapOf("type" to "string"), "pageSize" to mapOf("type" to "integer", "minimum" to 1, "maximum" to 200)))), schemaJson(mapOf("type" to "object")), "jetbrains.modules_list") { args, ctx ->
            val project = resolveProject(args["projectName"]?.toString())
            val items = readAction<List<Map<String, Any?>>>() {
                ModuleManager.getInstance(project).modules.map { module ->
                    mapOf("name" to module.name, "moduleFilePath" to module.moduleFilePath)
                }
            }
            ctx.resultHandleStore.buildItemsResult("jetbrains.modules_list", "modules", items, mapOf("kind" to "modules", "projectName" to project.name), clampNumber((args["pageSize"] as? Number)?.toInt() ?: 100, 1, 200))
        },
    )
    register(
        AiToolDefinition("jetbrains.files_list", "List files under one open project using the IntelliJ VFS.", schemaJson(mapOf("type" to "object", "additionalProperties" to false, "properties" to mapOf("projectName" to mapOf("type" to "string"), "maxResults" to mapOf("type" to "integer", "minimum" to 1, "maximum" to 20000), "pageSize" to mapOf("type" to "integer", "minimum" to 1, "maximum" to 200)))), schemaJson(mapOf("type" to "object")), "jetbrains.files_list") { args, ctx ->
            val project = resolveProject(args["projectName"]?.toString())
            val maxResults = clampNumber((args["maxResults"] as? Number)?.toInt() ?: 1000, 1, 20_000)
            val items = mutableListOf<Map<String, Any?>>()
            readAction<Unit> {
                val basePath = project.basePath?.let { LocalFileSystem.getInstance().findFileByPath(it) } ?: return@readAction
                VfsUtilCore.iterateChildrenRecursively(basePath, null) { file ->
                    if (!file.isDirectory) {
                        items.add(mapOf("path" to file.path, "name" to file.name))
                    }
                    items.size < maxResults
                }
            }
            ctx.resultHandleStore.buildItemsResult("jetbrains.files_list", "files", items, mapOf("kind" to "files", "projectName" to project.name), clampNumber((args["pageSize"] as? Number)?.toInt() ?: 100, 1, 200))
        },
    )
    register(
        AiToolDefinition("jetbrains.document_text_get", "Read one project file through the IntelliJ VFS.", schemaJson(mapOf("type" to "object", "required" to listOf("path"), "additionalProperties" to false, "properties" to mapOf("path" to mapOf("type" to "string"), "length" to mapOf("type" to "integer", "minimum" to 1, "maximum" to 32768)))), schemaJson(mapOf("type" to "object")), "jetbrains.document_text_get") { args, ctx ->
            val path = args["path"]?.toString()?.takeIf { it.isNotBlank() } ?: error("path is required.")
            val virtualFile = readAction { LocalFileSystem.getInstance().findFileByPath(path) } ?: error("File not found: $path")
            val text = readAction { VfsUtilCore.loadText(virtualFile) }
            ctx.resultHandleStore.buildTextResult("jetbrains.document_text_get", text, mapOf("kind" to "document", "path" to path), clampNumber((args["length"] as? Number)?.toInt() ?: 4096, 1, 32768))
        },
    )
    register(
        AiToolDefinition("jetbrains.psi_tree_get", "Inspect the PSI tree for one project file.", schemaJson(mapOf("type" to "object", "required" to listOf("path"), "additionalProperties" to false, "properties" to mapOf("projectName" to mapOf("type" to "string"), "path" to mapOf("type" to "string"), "maxNodes" to mapOf("type" to "integer", "minimum" to 1, "maximum" to 5000), "pageSize" to mapOf("type" to "integer", "minimum" to 1, "maximum" to 200)))), schemaJson(mapOf("type" to "object")), "jetbrains.psi_tree_get") { args, ctx ->
            val project = resolveProject(args["projectName"]?.toString())
            val path = args["path"]?.toString()?.takeIf { it.isNotBlank() } ?: error("path is required.")
            val maxNodes = clampNumber((args["maxNodes"] as? Number)?.toInt() ?: 1000, 1, 5000)
            val items = readAction<List<Map<String, Any?>>>() {
                val virtualFile = LocalFileSystem.getInstance().findFileByPath(path) ?: error("File not found: $path")
                val psiFile = PsiManager.getInstance(project).findFile(virtualFile) ?: error("PSI file not found: $path")
                val output = mutableListOf<Map<String, Any?>>()
                PsiTreeUtil.processElements(psiFile) { element ->
                    output.add(
                        mapOf(
                            "elementClass" to element.javaClass.simpleName,
                            "textRange" to mapOf("start" to element.textRange.startOffset, "end" to element.textRange.endOffset),
                            "textPreview" to element.text.take(80).replace("\n", "\\n"),
                        ),
                    )
                    output.size < maxNodes
                }
                output
            }
            ctx.resultHandleStore.buildItemsResult("jetbrains.psi_tree_get", "nodes", items, mapOf("kind" to "psiTree", "projectName" to project.name, "path" to path), clampNumber((args["pageSize"] as? Number)?.toInt() ?: 100, 1, 200))
        },
    )
    register(
        AiToolDefinition("jetbrains.run_configurations_list", "List run configurations for one open project.", schemaJson(mapOf("type" to "object", "additionalProperties" to false, "properties" to mapOf("projectName" to mapOf("type" to "string"), "pageSize" to mapOf("type" to "integer", "minimum" to 1, "maximum" to 200)))), schemaJson(mapOf("type" to "object")), "jetbrains.run_configurations_list") { args, ctx ->
            val project = resolveProject(args["projectName"]?.toString())
            val items = readAction<List<Map<String, Any?>>>() {
                RunManager.getInstance(project).allSettings.map { setting ->
                    mapOf(
                        "name" to setting.name,
                        "typeId" to setting.type.id,
                        "typeDisplayName" to setting.type.displayName,
                        "folderName" to setting.folderName,
                        "isShared" to setting.isShared,
                    )
                }
            }
            ctx.resultHandleStore.buildItemsResult("jetbrains.run_configurations_list", "runConfigurations", items, mapOf("kind" to "runConfigurations", "projectName" to project.name), clampNumber((args["pageSize"] as? Number)?.toInt() ?: 100, 1, 200))
        },
    )
}

private fun AiJetBrainsAgentAppService.refreshRegistry(force: Boolean = false): Map<String, Any?> {
    val fingerprint = generatedToolStore.computeFingerprint()
    if (!force && generatedToolsFingerprint == fingerprint && registry.count() > 0) {
        return mapOf(
            "reloaded" to false,
            "manifestHash" to registry.manifestHash(),
            "generatedToolCount" to generatedToolStore.listDefinitions().size,
        )
    }

    registry.reset()
    toolContext.registry = registry
    registerDefaultTools(registry, toolContext)
    registerDynamicGeneratedToolManagementTools(registry, toolContext)
    val generatedDefinitions = generatedToolStore.loadDefinitions()
    for (definition in generatedDefinitions) {
        registry.register(generatedToolStore.createToolDefinition(definition))
    }
    generatedToolsFingerprint = fingerprint
    runtimeState.log("info", "Generated tool registry refreshed. Loaded ${generatedDefinitions.size} generated tool(s).")
    return mapOf(
        "reloaded" to true,
        "manifestHash" to registry.manifestHash(),
        "generatedToolCount" to generatedDefinitions.size,
    )
}

private fun resolveProject(projectName: String?): Project {
    val projects = ProjectManager.getInstance().openProjects
    return when {
        projects.isEmpty() -> error("No open projects were found.")
        projectName.isNullOrBlank() -> projects.first()
        else -> projects.firstOrNull { it.name == projectName } ?: error("Open project not found: $projectName")
    }
}

private fun <T> readAction(block: () -> T): T = ReadAction.compute<T, RuntimeException> { block() }

private fun clampNumber(value: Int, minimum: Int, maximum: Int): Int = value.coerceIn(minimum, maximum)

private fun schemaJson(schema: Any): String = gson.toJson(schema)

private fun searchScore(tool: AiToolDefinition, tokens: List<String>): Int {
    if (tokens.isEmpty()) return 1
    val haystack = "${tool.id} ${tool.namespaceId} ${tool.description}".lowercase()
    var score = 0
    for (token in tokens) {
        score += when {
            tool.id.lowercase().contains(token) -> 5
            tool.description.lowercase().contains(token) -> 2
            haystack.contains(token) -> 1
            else -> 0
        }
    }
    return score
}

private fun isoTimestamp(): String = Instant.now().toString()
