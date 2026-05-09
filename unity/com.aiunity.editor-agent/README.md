# AI Unity Editor Agent

AI Unity Editor Agent 是一个运行在 Unity Editor 内部的、本地的、可发现且可扩展的 AI 工具运行层。它的目标不是单纯给 AI 暴露几个 Unity 接口，而是让 AI 真正进入 Unity 编辑器的工作上下文，具备发现能力、调用能力和自扩展能力。

当前仓库已经进一步抽象成 `AI Platform Agent Framework`。其中：

- `unity/com.aiunity.editor-agent/` 是 Unity 适配层，也就是当前的 AI Unity Editor Agent。
- `docs/framework/` 提供平台无关的协议与架构说明。
- `flutter/ai_flutter_agent/` 提供首个非 Unity 平台适配层，用同一套 discovery/call/result-handle 协议服务 Flutter 项目。

在普通的原生开发或网页开发场景中，AI 往往已经可以比较完整地阅读代码、目录和文本资源；但在 Unity 这样的游戏创作引擎里，很多关键上下文并不直接存在于源码中，而是存在于 Scene、Prefab、AssetDatabase、Selection、Console、编译状态、Importer 以及各种 Editor API 里。通用 AI 即使能看见项目文件，也往往只能看到表层，难以真正理解 Unity 项目的实时状态，更难直接执行引擎内部操作。

AI Unity Editor Agent 的价值就在于，它为 AI 和 Unity 之间建立了一层真正可执行的桥梁；而现在，这套桥梁也被整理成了可被其他平台复用的框架模式。

## 仓库结构

- 共享框架说明：仓库根目录下的 `docs/framework/README.md`
- 共享协议说明：仓库根目录下的 `docs/framework/AGENT_PROTOCOL.md`
- Unity 适配层：仓库根目录下的 `unity/com.aiunity.editor-agent/`
- Flutter 适配层：仓库根目录下的 `flutter/ai_flutter_agent/`

## 产品定位

AI Unity Editor Agent 是一个面向 Unity 项目的 AI 执行层。

它通过在 Unity Editor 内启动本地服务，把 Unity 原生能力组织成一组可被 AI 动态发现、按协议调用、按任务扩展的工具集合。AI 不再只是一个“会看代码的助手”，而是可以：

- 读取 Unity 项目的真实上下文，而不只是扫描文件。
- 调用 Unity Editor 自身的能力来查询、创建、修改和验证内容。
- 在内置工具不足时，为自己补充新的 Editor Tool，并继续完成任务。

换句话说，这个产品解决的不是“AI 会不会写 Unity 代码”，而是“AI 能不能真正使用 Unity”。

## 产品开发背景

随着 AI Coding Agent 在工程领域的能力持续增强，AI 已经可以很好地承担代码生成、代码理解、重构、调试、测试和自动化脚本编写等工作。但 Unity 项目和普通软件仓库不同，它并不是一个纯文本、纯代码驱动的环境。

Unity 项目的很多关键事实并不直观地写在代码里：

- 资源的关系依赖于 `AssetDatabase`。
- Scene 和 Prefab 的真实结构依赖 Unity 的序列化和运行时对象系统。
- 贴图、材质、模型、动画、预制体等资产很多都不是 AI 可以直接“看懂”的文本文件。
- 当前选中对象、编译状态、控制台报错、编辑器状态等信息只存在于 Unity Editor 运行时。
- 很多真实生产操作只能通过 Unity Editor API 或编辑器脚本完成。

这意味着，AI 在 Unity 场景里经常会遇到一个根本障碍：它也许能写出一段看起来正确的代码，但它并不知道项目里真实有哪些资产、这些资产彼此如何引用、当前场景里到底有什么对象、控制台到底报了什么错、某个 Prefab 是否真的生成成功。

AI Unity Editor Agent 就是在这个背景下设计出来的。它把 Unity 编辑器内部原本只能由人点击、由插件调用、或由定制脚本执行的能力，整理成 AI 可以稳定访问的工具系统，让 AI 第一次具备真正进入 Unity 工作流的基础设施。

## 这个产品解决了什么问题

AI Unity Editor Agent 主要解决以下几类问题：

- AI 只能读代码，却拿不到 Unity 编辑器中的真实项目状态。
- AI 能提出建议，却不能稳定执行 Unity 内部操作。
- AI 缺少合适工具时，必须等待人手动补插件或写 Editor Script。
- Unity 项目里大量隐性的编辑器工作流无法被 AI 复用、组合和自动化。
- AI 很难围绕 Scene、Prefab、资源依赖和控制台诊断开展完整任务闭环。

它填补的是 AI 与 Unity 引擎之间的执行鸿沟。

## 核心设计思想

### 1. 以可缓存的 manifest discovery 作为能力入口

AI 不应预设“Unity 一定有什么工具”，但也不应在每个任务里反复吞下完整 manifest。更好的方式是先读取轻量健康信息，比较 `manifestHash` 是否变化，再优先使用 manifest search、bundle 和按需 schema 描述接口去缩小能力范围。这样既能保持能力发现的准确性，也能显著降低 token 消耗。

### 2. 先使用现有能力，再按需扩展能力

如果 manifest 里已经有合适工具，AI 直接调用；如果没有，AI 可以生成新的 Editor Tool 脚本，通过标准约定注册进系统，等待 Unity 编译后重新出现在 manifest 中，再继续完成任务。这样，AI 的能力不是固定清单，而是可以随任务逐步增长。

### 3. 让 AI 通过 Unity 自己理解 Unity

这个产品并不假设 AI 天然“看懂”所有 Unity 资产，而是让 AI 借助 Unity 自己的 API、AssetDatabase、Scene、Prefab、Console 和编辑器状态去理解项目。也就是说，AI 获得的不是纯静态解析能力，而是 Unity 原生上下文中的执行能力。

### 4. 保持本地化和安全边界

服务默认只监听本地地址，支持 Token 校验，高风险工具支持确认弹窗，AI 生成的工具脚本被限制在指定目录内。产品目标不是放大权限，而是在可控边界内让 AI 真正可用。

## 核心能力

当前版本已经覆盖几类高价值能力：

- 服务与协议信息：健康检查、manifest 轻量摘要、manifest 全量兜底、manifest search、bundle discovery、按需 tool schema 描述、Agent 操作手册与简版协议、服务配置查看、最近日志和最近调用记录。
- 编译与诊断：读取编译状态、编译快照、错误摘要分组、分页错误详情、清理 Console、辅助定位执行失败原因。
- 资产能力：检索资源、分页返回搜索结果、分块读取文本资源、转换 GUID 与路径、分析依赖和反向依赖、刷新 AssetDatabase。
- 场景能力：读取当前选择对象、按路径选中资源、搜索场景对象、创建空对象、创建基础几何体、保存打开的场景。
- Prefab 自动化：通过 JSON 层级描述创建 Prefab，配置子节点、Transform、组件和序列化参数。
- 工具自扩展：生成模板、写入 AI 生成的 Editor Tool、列出已生成工具、删除生成工具。

这些能力组合起来之后，AI 不再只是能“输出一段 Unity 代码”，而是可以在 Unity 环境里实际执行一段完整任务。

## 典型工作方式

AI 使用这个产品时，通常遵循这样的流程：

1. 连接 Unity Editor 内的本地服务。
2. 先读取 `/health`，确认 `manifestHash` 是否变化。
3. 如果能力缓存可复用，直接继续；如果发生变化，优先调用 `/manifest/search` 或加载 bundle，而不是立即读取完整 manifest。
4. 对候选工具调用 `/tool/describe_many`，只获取真正要使用的 schema。
5. 根据任务选择现有工具执行，必要时先检查编译状态和错误摘要。
6. 如果没有合适工具，生成新的 Editor Tool 脚本并注册。
7. 等待 Unity 编译完成后重新检查 `manifestHash`，再继续 discovery 或调用。
8. 当工具返回 `resultHandle` 时，通过 `/result/{handleId}` 分页或分块获取后续数据，而不是让单次返回无限膨胀。

这套流程让 AI 的执行方式从“每轮重新吞完整能力目录”，变成“按需发现、按需描述、按需取数”的高质量低 token 工作流。

## 典型使用场景

这个工具非常适合以下工作场景：

- 在大型 Unity 项目中搜索某类资产、定位 Prefab、材质、脚本、贴图或配置文件。
- 分析某个资源被谁引用，或者它依赖了哪些其他资源。
- 在编译失败时获取 Unity Console 错误，结合代码与编辑器状态定位问题。
- 批量创建测试用场景对象、原型对象或基础关卡搭建元素。
- 用 JSON 或结构化描述快速生成标准化 Prefab。
- 读取当前 Selection 或场景对象状态，辅助 AI 在编辑器中做上下文感知决策。
- 为当前项目临时生成专用 Editor Tool，例如读取 Animator、检查 Addressables、扫描缺失引用、批量修复导入设置。
- 让 AI 辅助技术美术、工具开发、内容生产和研发支持团队处理重复性编辑器工作。

## 可扩展的更深层使用场景

这个产品的真正潜力并不止于当前内置工具，而在于它适合作为 Unity AI 工作流的基础设施继续扩展。比如：

- 项目规范检查：命名规则、目录结构、资源类型约束、组件缺失、引用丢失。
- 内容流水线自动化：批量拼装 Prefab、生成测试资源、布置基础场景结构、整理配置资产。
- 技术美术工具接入：材质规则检查、贴图导入设置校验、模型导入配置处理、特效资源审查。
- 动画与表现层分析：Animator Controller、Timeline、Playable、VFX Graph、Shader Graph 等专用工具。
- 项目交付与构建支持：Build Pipeline、Addressables、资源清单核查、构建前检查。
- 质量保障：查找高风险资产引用、扫描场景异常对象、辅助做编辑器级 QA。
- AI 按需补工具：当团队遇到新的 Unity 自动化需求时，不必先开发完整插件，而可以先让 AI 为当前任务生成最小可用工具。

这意味着它不仅是“一个 Unity AI 插件”，更可能成为 Unity 团队逐步沉淀 AI 工具生态的入口。

## 产品价值

### 1. 提升 AI 在 Unity 项目中的真实可用性

很多 AI 工具在普通工程里已经很强，但到了 Unity 环境里往往只能停留在代码层。AI Unity Editor Agent 让 AI 第一次获得 Unity 编辑器内部的执行触点，因此它不只是“更方便”，而是直接决定 AI 在 Unity 项目中是否真正可用。

### 2. 把隐性的编辑器工作流结构化

Unity 团队中大量工作依赖人工点击、临时脚本和个人经验。这些能力如果不能接口化，就很难复用，也很难交给 AI。这个产品把这些能力逐步工具化、协议化，让它们可以被调用、组合、记录和扩展。

### 3. 降低自动化和工具开发门槛

过去，团队想自动化一个 Unity 工作流，往往要先写一段 Editor 工具，再考虑入口、参数、执行和复用。现在可以先让 AI 调用已有工具，不足时再按需生成最小工具。这种方式显著降低了 Unity 自动化的起步成本。

### 4. 让 AI 不再受限于固定工具集

固定工具只能覆盖预设场景，但真实项目里的需求是不断变化的。AI Unity Editor Agent 的关键价值在于它允许 AI 在既有能力之外，为自己扩展新的 Unity 工具，从而在更复杂的项目环境中持续演进。

### 5. 为更完整的 Unity AI Agent 打基础

如果未来要让 AI 深度参与内容制作、技术美术、工具研发、项目诊断和编辑器流程自动化，那么首先必须解决“AI 如何进入 Unity 上下文”这个问题。AI Unity Editor Agent 本质上就是这层基础设施。

## 与普通 AI Coding Tool 的区别

普通 AI Coding Tool 更擅长：

- 阅读代码文件
- 理解前后端工程结构
- 生成脚本和配置
- 执行命令行任务
- 辅助测试、构建和调试

但 Unity 项目并不是一个只靠代码和命令行就能完整表达的系统。

AI Unity Editor Agent 的不同之处在于，它不是只让 AI “为 Unity 写代码”，而是让 AI “使用 Unity 编辑器本身”。这个差异决定了它更接近一个面向 Unity 的执行层，而不是一个通用代码助手。

## 安全默认值

为了让 AI 在 Unity 中可用，同时保持可控性，当前默认安全策略包括：

- 服务默认只监听 `127.0.0.1`。
- 默认要求通过 `X-AI-Agent-Token` 进行访问认证，同时继续兼容 `X-Unity-Ai-Token`。
- 高风险工具支持 Unity Editor 内确认弹窗。
- AI 生成的工具脚本被限制在 `Assets/Editor/AiUnityEditorAgent/GeneratedTools/`。
- 工具清单由 `[AiTool]` 方法自动发现生成，而不是手工维护。

## 安装

1. 下载并解压该包。
2. 在 Unity 中打开 **Window > Package Manager**。
3. 点击 **+ > Add package from disk...**。
4. 如果你直接使用这个 monorepo，选择 `unity/com.aiunity.editor-agent/package.json`；如果你单独分发 Unity 包，则选择该包根目录下的 `package.json`。
5. 打开 **Tools > AI Editor Agent > Control Center**。

## 默认服务信息

- 默认地址：`http://127.0.0.1:18777`
- Token 文件：`<ProjectRoot>/Library/AiUnityEditorAgent/token.txt`
- 健康与 discovery 元信息：`GET /health`
- 轻量工具清单：`GET /manifest`
- 全量工具清单兜底：`GET /manifest/full`
- 能力搜索：`POST /manifest/search`
- 能力 bundle：`GET /manifest/bundles` / `GET /manifest/bundle/{id}`
- 按需 schema 描述：`POST /tool/describe_many`
- 大结果分页 / 文本分块：`GET /result/{handleId}`
- 调用工具：`POST /call/{toolId}`
- Agent 简版协议：`GET /agent/brief`
- Agent 完整操作手册：`GET /agent` 或包内 `AGENT.md`

除 `/health` 外，默认所有请求都要求提供 `X-AI-Agent-Token`，并兼容旧的 `X-Unity-Ai-Token`。

## 面向 AI 的协议说明

`AGENT.md` 是 Unity 适配层内置的 AI 操作手册，定义了推荐的调用顺序、manifest 约定、工具实现约定以及基本安全规则。对于需要让外部 AI Agent 接入 Unity 的场景，`AGENT.md` 应视为运行协议的一部分，而不是普通补充文档。

如果需要看平台无关的抽象层，请直接查看：

- `docs/framework/README.md`
- `docs/framework/AGENT_PROTOCOL.md`
- `flutter/ai_flutter_agent/README.md`

## 总结

AI Unity Editor Agent 的本质，是把 Unity Editor 变成一个 AI 可进入、可发现、可调用、可扩展的本地工具运行环境。

它不是简单地给 AI 增加几个 Unity API，而是让 AI 获得围绕真实 Unity 项目上下文执行任务的能力。对于 Unity 这类高度依赖编辑器状态、引擎对象和资产关系的创作环境来说，这种能力不是附加功能，而是 AI 真正落地所需要的基础设施。
