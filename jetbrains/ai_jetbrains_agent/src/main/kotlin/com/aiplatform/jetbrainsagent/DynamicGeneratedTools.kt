package com.aiplatform.jetbrainsagent

import com.google.gson.GsonBuilder
import com.intellij.execution.RunManager
import com.intellij.openapi.application.ReadAction
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.module.ModuleManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.project.ProjectManager
import com.intellij.openapi.vfs.LocalFileSystem
import com.intellij.openapi.vfs.VfsUtilCore
import com.intellij.psi.PsiManager
import com.intellij.psi.util.PsiTreeUtil
import java.nio.charset.StandardCharsets
import java.nio.file.Files
import java.nio.file.Path
import javax.script.ScriptEngineManager
import kotlin.io.path.createDirectories
import kotlin.io.path.exists
import kotlin.io.path.extension
import kotlin.io.path.isRegularFile
import kotlin.io.path.name
import kotlin.io.path.readText
import kotlin.io.path.writeText

private val generatedToolsGson = GsonBuilder().setPrettyPrinting().create()

data class GeneratedToolDefinition(
    val fileName: String,
    val toolId: String,
    val description: String,
    val danger: String,
    val requiresConfirmation: Boolean,
    val argsSchema: Map<String, Any?>,
    val returnSchema: Map<String, Any?>,
    val source: String,
)

class JetBrainsGeneratedToolStore(
    private val config: AiJetBrainsAgentConfig,
    private val runtimeState: AiRuntimeState,
) {
    val definitionsDir: Path
        get() = config.stateDirectoryPath.resolve("generated_tools")

    fun ensureLayout() {
        definitionsDir.createDirectories()
    }

    fun computeFingerprint(): String {
        ensureLayout()
        val sb = StringBuilder()
        for (path in listFiles()) {
            sb.append(path.name).append('\n').append(path.readText()).append("\n---\n")
        }
        return sb.toString()
    }

    fun loadDefinitions(): List<GeneratedToolDefinition> {
        ensureLayout()
        return listFiles().map { readDefinition(it) }
    }

    fun listDefinitions(): List<Map<String, Any?>> =
        loadDefinitions().map { definition ->
            mapOf(
                "fileName" to definition.fileName,
                "toolId" to definition.toolId,
                "description" to definition.description,
                "danger" to definition.danger,
                "requiresConfirmation" to definition.requiresConfirmation,
            )
        }

    fun getTemplate(toolId: String = "generated.jetbrains_example", description: String = "Generated JetBrains tool.", fileName: String = ""): Map<String, Any?> {
        val safeToolId = if (toolId.isBlank()) "generated.jetbrains_example" else toolId
        return mapOf(
            "fileName" to normalizeFileName(if (fileName.isBlank()) suggestedFileName(safeToolId) else fileName),
            "definition" to mapOf(
                "toolId" to safeToolId,
                "description" to description,
                "danger" to "low",
                "requiresConfirmation" to false,
                "argsSchema" to mapOf(
                    "type" to "object",
                    "additionalProperties" to false,
                    "properties" to mapOf(
                        "projectName" to mapOf("type" to "string"),
                    ),
                ),
                "returnSchema" to mapOf(
                    "type" to "object",
                    "additionalProperties" to true,
                ),
                "source" to """
                    def projects = host.listProjects()
                    return [
                      message: "Hello from generated JetBrains tool",
                      projectCount: projects.size(),
                      activeEditorPath: host.activeEditorPath(args.projectName as String)
                    ]
                """.trimIndent(),
            ),
            "notes" to listOf(
                "The source body runs as a Groovy script with bindings: args, host, and json.",
                "Use host helper methods for common IDE queries, or import additional JDK/JetBrains classes as needed.",
                "Return plain JSON-serializable maps, lists, strings, booleans, or numbers.",
            ),
        )
    }

    fun upsertDefinition(fileName: String, definitionPayload: Any?): Map<String, Any?> {
        ensureLayout()
        val safeName = normalizeFileName(fileName)
        val definition = normalizeDefinition(definitionPayload, safeName)
        val targetPath = definitionsDir.resolve(safeName)
        targetPath.writeText(generatedToolsGson.toJson(definition.toPersistedJson()))
        return mapOf("fileName" to safeName, "path" to targetPath.toString())
    }

    fun deleteDefinition(fileName: String): Map<String, Any?> {
        ensureLayout()
        val safeName = normalizeFileName(fileName)
        val targetPath = definitionsDir.resolve(safeName)
        if (!targetPath.exists()) {
            return mapOf("deleted" to false, "fileName" to safeName)
        }
        Files.delete(targetPath)
        return mapOf("deleted" to true, "fileName" to safeName)
    }

    fun createToolDefinition(definition: GeneratedToolDefinition): AiToolDefinition =
        AiToolDefinition(
            id = definition.toolId,
            description = definition.description,
            argsSchemaJson = generatedToolsGson.toJson(definition.argsSchema),
            returnSchemaJson = generatedToolsGson.toJson(definition.returnSchema),
            handlerName = "generated:${definition.fileName}",
            danger = definition.danger,
            requiresConfirmation = definition.requiresConfirmation,
            handler = { args, _ -> executeDefinition(definition, args) },
        )

    private fun executeDefinition(definition: GeneratedToolDefinition, args: MutableMap<String, Any?>): Any? {
        val engine = ScriptEngineManager(javaClass.classLoader).getEngineByName("groovy")
            ?: error("Groovy script engine is not available.")
        val bindings = engine.createBindings()
        bindings["args"] = args
        bindings["host"] = JetBrainsGeneratedToolHost()
        bindings["json"] = generatedToolsGson
        return engine.eval(definition.source, bindings)
    }

    private fun readDefinition(path: Path): GeneratedToolDefinition {
        val raw = generatedToolsGson.fromJson(path.readText(StandardCharsets.UTF_8), Map::class.java)
        return normalizeDefinition(raw, path.name)
    }

    private fun listFiles(): List<Path> =
        Files.list(definitionsDir).use { stream ->
            stream
                .filter { it.isRegularFile() && it.extension.equals("json", ignoreCase = true) }
                .sorted()
                .toList()
        }

    private fun normalizeDefinition(raw: Any?, fileName: String): GeneratedToolDefinition {
        if (raw !is Map<*, *>) error("definition must be an object.")
        val toolId = raw["toolId"]?.toString()?.trim().orEmpty()
        val description = raw["description"]?.toString()?.trim().orEmpty()
        val source = raw["source"]?.toString().orEmpty()
        if (toolId.isBlank()) error("definition.toolId is required.")
        if (description.isBlank()) error("definition.description is required.")
        if (source.isBlank()) error("definition.source is required.")
        return GeneratedToolDefinition(
            fileName = normalizeFileName(fileName),
            toolId = toolId,
            description = description,
            danger = raw["danger"]?.toString()?.ifBlank { "low" } ?: "low",
            requiresConfirmation = raw["requiresConfirmation"] as? Boolean ?: false,
            argsSchema = ensureSchemaObject(raw["argsSchema"]),
            returnSchema = ensureSchemaObject(raw["returnSchema"]),
            source = source,
        )
    }

    private fun normalizeFileName(fileName: String): String {
        val trimmed = fileName.trim()
        if (trimmed.isBlank()) error("fileName is required.")
        val safeName = Path.of(trimmed).fileName.toString()
        if (safeName != trimmed) error("Subdirectories are not allowed. Provide only a file name.")
        if (!safeName.endsWith(".json", ignoreCase = true)) error("Generated tool files must use the .json extension.")
        return safeName
    }

    private fun suggestedFileName(toolId: String): String =
        toolId.map { c -> if (c.isLetterOrDigit() || c == '.' || c == '_' || c == '-') c else '_' }.joinToString("") + ".json"

    private fun ensureSchemaObject(value: Any?): Map<String, Any?> =
        if (value is Map<*, *>) value.entries.associate { it.key.toString() to it.value } else mapOf("type" to "object")

    private fun GeneratedToolDefinition.toPersistedJson(): Map<String, Any?> = mapOf(
        "toolId" to toolId,
        "description" to description,
        "danger" to danger,
        "requiresConfirmation" to requiresConfirmation,
        "argsSchema" to argsSchema,
        "returnSchema" to returnSchema,
        "source" to source,
    )
}

private class JetBrainsGeneratedToolHost {
    fun workspaceSummary(): List<Map<String, Any?>> = readIdeAction {
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

    fun listProjects(): List<Map<String, Any?>> = readIdeAction {
        ProjectManager.getInstance().openProjects.map { project ->
            mapOf("name" to project.name, "basePath" to project.basePath)
        }
    }

    fun activeEditorPath(projectName: String?): String? = readIdeAction {
        val project = resolveProjectForGeneratedTools(projectName)
        val selectedEditor = FileEditorManager.getInstance(project).selectedTextEditor
        selectedEditor?.let { editor -> FileDocumentManager.getInstance().getFile(editor.document)?.path }
    }

    fun listModules(projectName: String?): List<Map<String, Any?>> = readIdeAction {
        val project = resolveProjectForGeneratedTools(projectName)
        ModuleManager.getInstance(project).modules.map { module ->
            mapOf("name" to module.name, "moduleFilePath" to module.moduleFilePath)
        }
    }

    fun listFiles(projectName: String?, maxResults: Int = 1000): List<Map<String, Any?>> = readIdeAction {
        val project = resolveProjectForGeneratedTools(projectName)
        val safeMax = clampNumber(maxResults, 1, 20_000)
        val items = mutableListOf<Map<String, Any?>>()
        val basePath = project.basePath?.let { LocalFileSystem.getInstance().findFileByPath(it) } ?: return@readIdeAction emptyList()
        VfsUtilCore.iterateChildrenRecursively(basePath, null) { file ->
            if (!file.isDirectory) {
                items.add(mapOf("path" to file.path, "name" to file.name))
            }
            items.size < safeMax
        }
        items
    }

    fun readText(path: String, length: Int = 0): String = readIdeAction {
        val virtualFile = LocalFileSystem.getInstance().findFileByPath(path) ?: error("File not found: $path")
        val text = VfsUtilCore.loadText(virtualFile)
        if (length > 0) text.take(clampNumber(length, 1, 32768)) else text
    }

    fun psiTree(projectName: String?, path: String, maxNodes: Int = 1000): List<Map<String, Any?>> = readIdeAction {
        val project = resolveProjectForGeneratedTools(projectName)
        val safeMax = clampNumber(maxNodes, 1, 5000)
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
            output.size < safeMax
        }
        output
    }

    fun listRunConfigurations(projectName: String?): List<Map<String, Any?>> = readIdeAction {
        val project = resolveProjectForGeneratedTools(projectName)
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
}

fun registerDynamicGeneratedToolManagementTools(registry: AiToolRegistry, context: AiToolExecutionContext) {
    fun register(tool: AiToolDefinition) = registry.register(tool)

    register(
        AiToolDefinition(
            "tool.get_template",
            "Return a safe template for a generated JetBrains tool definition.",
            generatedToolsGson.toJson(
                mapOf(
                    "type" to "object",
                    "additionalProperties" to false,
                    "properties" to mapOf(
                        "toolId" to mapOf("type" to "string"),
                        "description" to mapOf("type" to "string"),
                        "fileName" to mapOf("type" to "string"),
                    ),
                ),
            ),
            generatedToolsGson.toJson(mapOf("type" to "object")),
            "tool.get_template",
        ) { args, ctx ->
            ctx.generatedToolStore.getTemplate(
                toolId = args["toolId"]?.toString().orEmpty().ifBlank { "generated.jetbrains_example" },
                description = args["description"]?.toString().orEmpty().ifBlank { "Generated JetBrains tool." },
                fileName = args["fileName"]?.toString().orEmpty(),
            )
        },
    )
    register(
        AiToolDefinition(
            "tool.list_generated",
            "List generated JetBrains tool definitions available to the adapter.",
            generatedToolsGson.toJson(
                mapOf(
                    "type" to "object",
                    "additionalProperties" to false,
                    "properties" to mapOf("pageSize" to mapOf("type" to "integer", "minimum" to 1, "maximum" to 200)),
                ),
            ),
            generatedToolsGson.toJson(mapOf("type" to "object")),
            "tool.list_generated",
        ) { args, ctx ->
            ctx.resultHandleStore.buildItemsResult(
                "tool.list_generated",
                "items",
                ctx.generatedToolStore.listDefinitions(),
                mapOf("kind" to "generatedTools", "platformId" to "jetbrains"),
                clampNumber((args["pageSize"] as? Number)?.toInt() ?: 100, 1, 200),
            )
        },
    )
    register(
        AiToolDefinition(
            "tool.upsert_generated",
            "Create or update one generated JetBrains tool definition and reload the manifest.",
            generatedToolsGson.toJson(
                mapOf(
                    "type" to "object",
                    "required" to listOf("fileName", "definition"),
                    "additionalProperties" to false,
                    "properties" to mapOf(
                        "fileName" to mapOf("type" to "string"),
                        "definition" to mapOf("type" to "object"),
                    ),
                ),
            ),
            generatedToolsGson.toJson(mapOf("type" to "object")),
            "tool.upsert_generated",
            danger = "high",
            requiresConfirmation = true,
        ) { args, ctx ->
            val result = ctx.generatedToolStore.upsertDefinition(args["fileName"]?.toString().orEmpty(), args["definition"])
            val reloadResult = ctx.reloadGeneratedTools()
            result + mapOf(
                "manifestHash" to reloadResult["manifestHash"],
                "generatedToolCount" to reloadResult["generatedToolCount"],
                "message" to "Generated JetBrains tool definition written and manifest reloaded.",
            )
        },
    )
    register(
        AiToolDefinition(
            "tool.delete_generated",
            "Delete one generated JetBrains tool definition and reload the manifest.",
            generatedToolsGson.toJson(
                mapOf(
                    "type" to "object",
                    "required" to listOf("fileName"),
                    "additionalProperties" to false,
                    "properties" to mapOf("fileName" to mapOf("type" to "string")),
                ),
            ),
            generatedToolsGson.toJson(mapOf("type" to "object")),
            "tool.delete_generated",
            danger = "high",
            requiresConfirmation = true,
        ) { args, ctx ->
            val result = ctx.generatedToolStore.deleteDefinition(args["fileName"]?.toString().orEmpty())
            val reloadResult = ctx.reloadGeneratedTools()
            result + mapOf(
                "manifestHash" to reloadResult["manifestHash"],
                "generatedToolCount" to reloadResult["generatedToolCount"],
            )
        },
    )
    register(
        AiToolDefinition(
            "tool.reload_generated",
            "Force a generated-tool rescan and manifest rebuild.",
            generatedToolsGson.toJson(mapOf("type" to "object", "additionalProperties" to false, "properties" to emptyMap<String, Any>())),
            generatedToolsGson.toJson(mapOf("type" to "object")),
            "tool.reload_generated",
        ) { _, ctx ->
            ctx.reloadGeneratedTools()
        },
    )
}

private fun resolveProjectForGeneratedTools(projectName: String?): Project {
    val projects = ProjectManager.getInstance().openProjects
    return when {
        projects.isEmpty() -> error("No open projects were found.")
        projectName.isNullOrBlank() -> projects.first()
        else -> projects.firstOrNull { it.name == projectName } ?: error("Open project not found: $projectName")
    }
}

private fun <T> readIdeAction(block: () -> T): T = ReadAction.compute<T, RuntimeException> { block() }
