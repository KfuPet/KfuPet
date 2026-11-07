# AGENTS.md

# KfuPet AI 开发规范

版本：3.0

> 语言：[English](../AGENTS.md) | **简体中文**

> 本文档为英文版的简体中文翻译。若两者内容不一致，以英文版为准。

> 若本文档与用户的明确指令冲突，始终以用户最新的指令为准。

本文档定义每位 AI 编码助手在参与 KfuPet 项目时必须遵守的开发规则。

目标是让项目架构在整个开发周期内保持整洁、可维护且一致。

---

# 项目信息

项目名称

KfuPet

项目类型

AI 驱动的二次元桌面伙伴（桌宠）平台。

当前版本

v0.1.6（Beta，已随安装器与更新器发布）

目标平台

Windows 10 / 11（64 位）

框架

WPF（.NET 8.0）

语言

C#

IDE

Visual Studio 2022

未来平台

Android（独立项目）

桌面端与 Android 端是完全独立的两个项目。

除非明确要求，不要尝试将两者合并为一个项目。

---

# 已实现系统

以下系统已在代码库中实现：

- 透明、无边框、置顶的桌面窗口
- 基于固定人形骨骼与贴图挂点的角色渲染
- 从 `Characters/` 动态加载角色包
- 鼠标交互：拖动、越界回弹、触摸反应
- 通过 OpenAI 兼容接口进行 AI 对话（模型可配置）
- 通过 LLamaSharp 使用 GGUF 文件进行本地模型推理
- 模型服务商切换（可配置多个模型，同时只有一个生效）
- 记忆系统：短期记忆、归档记忆、长期记忆
- 情绪系统：对话后情绪判断与触摸反应
- 基于闲置状态与前台应用的主动搭话
- 工具调用（函数调用）与工具注册表
- 角色画廊、聊天记录、长期记忆窗口
- 托盘菜单、设置窗口、明暗主题资源
- 更新检查（GitHub → Gitee 回退）与外部更新器
- 启动公告与日志服务
- 开发者模式，以及供外部开发者工具使用的命名管道调试 IPC

这些系统是当前的基线。请在其基础上扩展，而不是替换。

---

# 未来系统

以下内容已规划但尚未实现。除非明确要求，不要实现它们：

- 语音 / TTS
- 骨骼动画运行时（`Animation` / `Keyframe` 模型已存在但未被使用）
- 表情系统（`Expression` 模型已存在但未被使用）
- 插件系统
- 角色仓库
- AI 超分辨率
- 开发者 SDK
- 角色编辑器
- 性能监视器
- 鼠标穿透模式与自适应窗口尺寸
- 视觉服务（`VisionService` 目前是空壳）
- Android 调试 HTTP 服务

`docs/reference/备忘录.md` 记录了这些空壳。某个空壳变为真实实现时，请同步更新该文档。

---

# 开发理念

先保证代码能跑通。

其次考虑架构。

最后再做优化。

始终优先选择：

能正常工作的代码

而不是

完美的代码。

避免过度设计。

保持实现简单。

---

# 通用原则

优先级：

1. 正确性

2. 可读性

3. 可维护性

4. 性能

绝不要为了不必要的优化牺牲可读性。

---

# 编码风格

始终：

- 类名使用 PascalCase。
- 公共成员使用 PascalCase。
- 局部变量使用 camelCase。
- 私有字段加 `_` 前缀。
- 在合适场景使用 async/await。
- 公共 API 添加 XML 文档注释。
- 保持方法简短且职责单一。

绝不：

- 重复代码。
- 使用魔法数字。
- 硬编码资源。
- 硬编码 AI 服务商。
- 硬编码配置值。
- 阻塞 UI 线程。

---

# 项目结构

本仓库是一个位于仓库根目录的单 WPF 工程：

```
KfuPet.slnx                  解决方案文件
KfuPet.csproj                单工程，RootNamespace = KfuPet
App.xaml / App.xaml.cs       应用启动与生命周期
MainWindow.xaml / .cs        桌宠窗口
SplashWindow.xaml / .cs      启动闪屏
GlobalUsings.cs              项目级全局 using

Assets/                      图标与静态 UI 图片
Characters/                  内置角色包（每个角色一个文件夹）
Controls/                    自定义 WPF 控件（如 CharacterCanvas）
Core/
    Math/                    Matrix3x3 与数学基础
    Memory/                  记忆条目与存储
    Rendering/               RenderContext、Renderer、SkeletonRenderer
Helpers/                     MarkdownRenderer、ModelReplyText
Models/                      数据模型（角色、骨骼、AI、设置、更新等）
Properties/                  程序集与工程元数据
Resources/                   XAML 资源字典（颜色、字体、对话框样式）
Services/
    Commands/                IPC 命令模型
    Ipc/                     命名管道服务端与客户端
    Tools/                   ITool 实现与 ToolRegistry
    ...                      业务服务
Views/                       窗口与对话框（XAML + 后置代码）
docs/                        项目文档
```

运行时数据不属于仓库，统一放在缓存根目录下：

```
%AppData%\KfuPet\            默认缓存根目录
    Config\                  settings.json、models.json、stopwords.json、deployed-models.json
    Memory\                  短期记忆、归档记忆、长期记忆文件
    Logs\                    KfuPet-*.log
    Models\                  已下载的 GGUF 文件
```

缓存根目录可由用户修改，记录在 `HKCU\Software\KfuPet` 的 `CacheRoot` 值中。

绝不要在仓库内创建 `Config/` 文件夹。请使用 `AppPaths` 解析运行时路径。

除非明确要求，不要将工程拆分为多个项目。

---

# 架构

职责分离。

UI 层（`Views/`、`Controls/`）：

仅负责：

- 渲染
- 窗口与对话框
- 用户交互
- 动画与视觉状态播放

业务逻辑（`Services/`）：

负责：

- AI 与模型调用
- 角色行为
- 记忆
- 情绪
- 工具
- 配置
- 更新与日志

核心层（`Core/`、`Models/`）：

- 共享数据结构
- 数学、记忆存储与渲染基础能力

规则：

- 优先使用 MVVM，但现有视图后置代码可以处理纯界面事务。
- 绝不要把业务逻辑写进 XAML 后置代码。请移到服务层。
- 避免服务之间出现循环依赖。共享状态应放在 `SettingsService` 或专门的服务中。
- 业务逻辑必须位于服务层。视图后置代码可以编排服务调用（`MainWindow` 目前就是如此），但不得包含服务商、记忆、工具或配置逻辑。

---

# 角色系统

角色资源（图片、动画、表情、元数据、提示词）必须动态加载。

绝不硬编码角色资源或角色专属路径。

角色包是 `Characters/<CharacterId>/` 下的一个文件夹：

- `character.json` — id、名称、版本、作者、描述、预览图、标签、提示词入口
- `prompt.md` — 角色提示词与人设
- `reactions.json` — 触摸 / 交互反应
- `attachments.json` — 绑定到骨骼的贴图挂点

完整格式定义在 `docs/guides/角色包制作指南.md`。请遵循该文档，不要自行发明新字段。

本项目的角色骨骼（骨骼层级）是固定的——只支持人形角色——因此骨骼定义可以硬编码在代码中。

调试骨骼线框（`ShowDebugBones`）是用于将模型对齐到骨骼参考的长期创作者工具。它默认关闭，从设置 / 开发者工具中切换。不要把它当作临时调试代码删除。

新增或修改角色包字段时，必须同时更新角色包制作指南与加载器。

---

# 骨骼与渲染

渲染使用原生 WPF（`DrawingContext`、`Canvas`、`Shape`）以及带缓存的位图挂点。

- `Core/Rendering/RenderContext` 持有绘制表面。
- `Core/Rendering/SkeletonRenderer` 绘制骨骼、挂点与调试线框。
- `Controls/CharacterCanvas` 暴露 `Skeleton` 与 `ShowDebugBones` 依赖属性 / CLR 属性。

规则：

- 未经明确许可，不要用 SkiaSharp、Direct2D 或游戏引擎替换渲染栈。
- 保留透明度命中检测与位图缓存，它们可以避免逐帧重复解码。
- 不要在渲染路径中引入逐帧分配。
- 渲染必须留在 UI 线程；耗时的图片解码或文件 IO 必须移到后台。

---

# 动画

动画运行时尚未实现。

当前状态：

- `Models/Animation.cs`、`Models/Keyframe.cs`、`Models/AnimationState.cs`、`Models/Expression.cs` 已存在，但都是未使用的占位模型。
- 除非明确要求，不要实现骨骼动画播放器、表情系统或物理系统。

实现动画时，必须支持：

- 骨骼
- 贴图挂点
- 动画片段
- 表情
- 物理

请保持现有模型类型可作为起点使用。

---

# AI 服务商

AI 服务商必须始终可替换。

远程服务商使用 OpenAI 兼容的 `/chat/completions` 接口。

规则：

- 绝不硬编码服务商、接口地址、模型名称或 API 密钥。
- 所有远程调用都必须经过 `ChatService`，并使用 `ModelConfigService` 中当前生效的 `ModelConfig`。
- 新服务商必须能通过 `BaseUrl` + `ApiKey` + `ModelId` 表达；除非服务商确实有特殊要求，否则不要新增服务商专属分支。
- API 密钥通过 `ProtectedStringConverter` 使用 DPAPI 加密。绝不绕过它，绝不记录密钥，绝不把密钥写成明文。
- 工具调用使用 OpenAI 兼容的 `tools` 结构。不要另造第二套工具调用协议。
- 每个新的 AI 调用点都必须声明 `ModelCallKind` 与 `TokenUsageCategory`，以确保用量统计与用户设置继续生效。
- 非主对话调用点必须遵守 `SettingsService.GetModelSource(kind)`；不要自行读取设置后硬编码 `Local` 或 `Remote`。

主对话始终使用当前生效的远程模型。本地模型仅按调用点选择性启用。

---

# 本地模型

本地推理使用 LLamaSharp 与 GGUF 权重。

已批准的包（未经许可，不要修改版本或更换运行时）：

- `LLamaSharp` 0.27.0
- `LLamaSharp.Backend.Cpu` 0.27.0
- `LLamaSharp.Backend.Vulkan` 0.27.0

关键文件：

- `Models/InferenceDevice.cs` — `Auto` / `Gpu` / `Cpu`
- `Models/HardwareInfo.cs` — 用于推荐档位的硬件摘要
- `Models/LocalModelOption.cs` — 可下载模型目录条目
- `Models/DeployedModel.cs` — 已下载模型记录
- `Services/HardwareProbe.cs` — CPU / 内存 / 独显显存检测
- `Services/LocalModelCatalog.cs` — 内置模型目录与推荐规则
- `Services/LocalModelDownloadService.cs` — 带镜像回退与 SHA256 的下载
- `Services/LocalModelDeploymentService.cs` — `deployed-models.json` 与文件管理
- `Services/LocalModelRuntime.cs` — LLamaSharp 运行时
- `Services/LocalModelService.cs` — 启用 / 停用 / 加载 / 释放协调
- `Services/ChatService.cs` — 本地优先推理与远端回退

运行时规则：

- 同一时间只允许加载一个本地模型，且同一时间只允许一路推理（`LocalModelRuntime` 闸门）。
- 上下文长度为 8192。修改前必须评估 8B 模型的内存影响。
- GPU 层数必须通过 `LocalModelRuntime.ResolveGpuLayerCount` 计算。不要在其它位置硬编码层数。
- `Auto` 仅在独显显存 ≥ 模型文件体积 × 1.2 时使用 GPU，否则回退到 CPU。
- 模型加载绝不能运行在 UI 线程。加载在后台任务中完成。
- 停用、切换、删除模型以及应用退出时必须释放模型。绝不要在存在启用记录的情况下长期占着已加载模型。
- 下载地址必须保持"国内镜像优先、官方地址兜底"的顺序；不要删除兜底地址。

调用规则：

- 只有声明了 `ModelCallKind` 的调用点才允许使用本地推理。
- 以下情况本地调用必须回退到远程模型：没有启用的模型、模型能力不足、运行时未加载、短句调用遇到推理繁忙、推理超时、推理抛异常。
- 绝不把本地推理失败作为用户可见错误抛出；记录日志并回退。
- `Qwen3-1.7B` 不支持记忆提取与记忆总结。请保持 `SupportsMemoryExtraction` / `SupportsMemorySummarization` 标记准确。
- 情绪判断（`ModelCallKind.EmotionJudge`）不得保存为 `Local`；它尚未经过本地模型稳定性测试。
- 新增本地模型或调整其能力时，必须同步更新 `docs/reference/模型API调用点清单.md`。

---

# 记忆系统

记忆是本项目的核心系统之一。

当前实现：

- `Core/Memory/` — 记忆条目与存储
- `Services/MemorySystem.cs` — 提取、评分、总结、归档流程
- `Services/MemoryManager.cs` — 高层记忆操作
- `Services/MemoryTriggerService.cs` — 触发词
- `Services/StopWordsService.cs` — 检索停用词
- 文件存放在 `%AppData%\KfuPet\Memory`（或用户自定义的缓存根目录）

记忆层级：

- 短期记忆
- 归档记忆
- 长期记忆

规则：

- 记忆存储必须与 AI 服务商保持独立。
- 不要让记忆与情绪耦合。
- 没有迁移方案时，不要修改磁盘上的记忆格式。
- 记忆提取必须遵守本地模型能力标记。
- 新的记忆调用必须传入正确的 `ModelCallKind` 与 `TokenUsageCategory`。
- 记忆操作不得阻塞 UI 线程。

未来支持（尚未实现）：

- 可检索 / 向量记忆
- 记忆重要性调优界面
- 记忆过期

除非被要求，不要实现这些内容。

---

# 情绪系统

情绪应保持独立。

当前实现：

- `Services/EmotionService.cs` — 情绪状态与衰减
- `Services/EmotionJudgeService.cs` — 对话后情绪判断
- `Models/EmotionState.cs`

情绪影响：

- 动画
- 表情
- 对话
- 行为
- 语音（未来）

规则：

- 不要让情绪与记忆紧密耦合。
- 情绪变化必须经过 `EmotionService`；不要从视图直接修改情绪状态。
- 情绪判断目前仅允许远程模型。
- 本地模型的改动不得悄悄改变情绪行为。

---

# 工具与函数调用

工具定义在 `Services/Tools/`，并注册到 `ToolRegistry`。

当前已注册工具：

- `WebSearchTool`
- `BalanceTool`
- `EmotionTool`
- `FeedTool`
- `MemorySearchTool`
- `OpenAppTool`
- `ListRunningAppsTool`

规则：

- 新工具必须实现 `ITool`，并在主窗口的工具注册表中注册。
- 工具名称与参数结构属于面向模型的契约。未经提示词与文档同步更新，不要重命名。
- 未经用户明确确认，工具绝不执行破坏性或高权限操作。
- `OpenAppTool` 必须遵守允许启动的应用列表；完全访问权限必须走现有的确认流程。
- 不要添加不受用户设置过滤的隐藏工具。
- 工具不得阻塞 UI 线程，并在适用时支持取消。
- `WebSearchTool` 复用当前生效模型的服务商密钥，调用服务商官方搜索接口（DeepSeek 或 Kimi 的 `/v1/tools/search`）。必须返回真实结果或明确的失败提示，绝不编造搜索结果。

---

# IPC 与开发者模式

IPC 用于外部开发者工具（`KfuPet-Tool`）。

实现：

- `Services/Ipc/NamedPipeServer.cs` — `KfuPet.Skeleton` 命令管道
- `Services/Ipc/LogPipeServer.cs` / `LogPipeClient.cs` — `KfuPet.Log` 日志流
- `Services/CommandDispatcher.cs` — 按 `service` 字段分发命令
- `Services/DeveloperModeService.cs` — 开发者模式开关
- `docs/skeleton/SKELETON_API.md` — 协议契约

规则：

- 不要破坏现有管道名称、命令名称或负载结构。
- 任何协议变更都必须同步更新 `docs/skeleton/SKELETON_API.md`。
- 命令处理器必须校验输入并返回错误，而不是把异常抛进管道循环。
- 开发者模式必须保持默认关闭。

---

# 窗口

当前实现：

- 透明
- 无边框
- 置顶
- 可拖动，越界时回弹
- 托盘菜单

未来（尚未实现）：

- 自适应窗口尺寸
- 鼠标穿透模式
- 窗口动画

只实现被要求的功能。

---

# 配置

避免硬编码配置。

当前配置为缓存根目录下的 JSON 文件：

- `Config\settings.json` — 用户设置
- `Config\models.json` — 远程模型配置
- `Config\stopwords.json` — 检索停用词
- `Config\deployed-models.json` — 已下载的本地模型

规则：

- 通过 `AppPaths` 解析路径；绝不硬编码 `%AppData%` 路径。
- 通过 `SettingsService` 读写设置；不要从视图直接打开 `settings.json`。
- 新增配置文件时，必须同时提供迁移方案与默认路径。
- 配置变更必须向后兼容，或迁移旧值。
- 绝不提交运行时配置或密钥。

---

# 资源

尽可能动态加载资源。

绝不硬编码资源路径。

资源位置：

- `Assets/` — 图标与静态 UI 图片
- `Resources/` — XAML 资源字典（颜色、字体、对话框样式）
- `Characters/` — 角色包资源

规则：

- 主题颜色与样式使用资源字典；不要在视图中硬编码颜色。
- 角色图片属于角色包，不要放进 `Assets/`。
- 未经更新所有引用，不要重命名或移动现有资源。

---

# 性能

避免：

- 阻塞 UI 线程
- 无限轮询
- 不必要的分配
- 频繁创建对象
- 并行本地推理

优先：

- 异步编程
- 延迟加载
- 缓存

规则：

- 本地模型加载与推理必须在后台任务中运行。
- 不要在 UI 线程发起 AI 请求。
- 不要添加持续轮询的计时器；使用事件或一次性定时任务。
- 在合理范围内保持渲染路径无额外分配。
- 不要过早优化。

---

# 安全

绝不：

- 硬编码 API 密钥
- 硬编码密钥
- 在源码中保存密码
- 记录 API 密钥、包含密钥的提示词或完整请求体

始终：

- 从配置加载密钥。
- API 密钥必须通过 `ProtectedStringConverter` 使用 DPAPI 加密。
- 打开用户或模型提供的链接前，仅放行 `http` / `https`。
- 打开或删除文件前校验文件路径。
- 把模型传入的工具参数视为不可信输入。

---

# 第三方库

当前批准的依赖：

- LLamaSharp（CPU + Vulkan 后端），用于本地 GGUF 推理

其它能力应使用 .NET 8 / WPF 内置技术栈。

规则：

- 未经明确许可，不要引入 SkiaSharp、NAudio、SQLite、CommunityToolkit.Mvvm 或其它重量级依赖。
- 未经许可，不要升级 NuGet 包。
- 不要引入 Preview 或 Experimental 包。
- 未经许可，不要修改 `.csproj` 中的包版本。

---

# 全局 using

`KfuPet.csproj` 已关闭 `ImplicitUsings`。

`GlobalUsings.cs` 仅提供：

- `System`
- `System.Collections.Generic`
- `System.IO`
- `System.Linq`
- `System.Net.Http`
- `System.Threading`
- `System.Threading.Tasks`

规则：

- 其它命名空间必须显式添加 `using`。
- 已启用 `UseWindowsForms`，因此 `System.Windows.Forms` 与 `System.Drawing` 可能与 WPF 类型冲突。必要时使用完全限定名或别名。
- 未经许可，不要启用 `ImplicitUsings`。

---

# Git 规范

提交保持聚焦。

不要修改无关文件。

除非被要求，不要重命名文件。

除非被要求，不要删除现有代码。

尊重当前项目架构。

提交信息格式（来自 `CONTRIBUTING.md`）：

```
type：summary
```

- 使用全角冒号 `：`。
- 类型：`feat`、`fix`、`docs`、`chore`。
- 一次提交只做一件事。不要混入无关改动。
- 绝不提交 `bin/`、`obj/`、运行时 `Config/`、日志、模型文件或密钥。

---

# 文档

新增系统时：

- 用户可见行为变化时更新 `README.md`。
- 开发流程或项目结构变化时更新 `CONTRIBUTING.md`。
- 为公共 API 添加 XML 注释。
- 保持文档与实现同步。

相关文档：

- `docs/guides/角色包制作指南.md` — 角色包格式
- `docs/skeleton/SKELETON_API.md` — IPC / 骨骼协议
- `docs/skeleton/SKELETON_BONES.md` — 骨骼参考
- `docs/reference/模型API调用点清单.md` — 模型调用点与本地模型能力矩阵
- `docs/design/情感系统与DPI缩放.md` — 情感系统与 DPI 设计说明
- `docs/reference/备忘录.md` — 空壳与未使用模型

规则：

- 修改上述文档所描述的行为时，必须在同一次改动中同步更新对应文档。
- 不要为一次性记录创建新的文档文件；请扩展现有文档。

---

# 代码校验

每次修改代码后：

AI 助手必须在结束任务前检查明显的代码问题。

始终检查：

- 语法错误。
- 缺失的 using（注意 `ImplicitUsings` 已关闭）。
- 未定义的变量。
- 未定义的方法。
- 错误的类或命名空间引用。
- 不匹配的方法签名。
- XAML 名称 / 事件处理器不匹配。
- 不存在的资源键。
- 可静态检测的常见 C# 编译错误。

如果发现错误：

AI 必须立即修复，然后再结束任务。

不要故意留下已知的编译错误。

AI 不要求必须构建项目。

用户会在 Visual Studio 中构建并运行项目。

修改现有代码时，始终检查改动是否要求同步更新其它相关文件。

例如：

- 命名空间变更
- 构造函数参数变更
- 接口变更
- 方法签名变更
- 类重命名
- 配置变更
- 设置结构变更
- 角色包格式变更
- IPC 协议变更

如果相关文件也需要更新，不要只改一个文件。

完成任务前，始终复查改动代码的诊断信息。

如果出现任何编译期诊断，结束前必须修复。

不要故意留下已知错误的代码。

绝不要在尚未自行检查修改代码的情况下，让用户先去编译项目。

在认为任务完成前，始终对生成的代码做一次自查。

---

# WPF 规范

尽可能优先使用 WPF 原生能力。

使用：

- `AllowsTransparency`
- `WindowStyle=None`
- `Background=Transparent`
- WPF 原生图形、`DrawingContext` 与资源字典

优先使用 MVVM。

保持 UI 与业务逻辑分离。

除非必要，避免无谓的 Win32 API 调用。硬件探测与缓存根目录设置所需的注册表读取属于可接受例外；新增互操作代码时必须说明理由。

正确使用 `Dispatcher`：

- UI 更新放在 UI 线程。
- 后台工作放在 `Task.Run` 或异步服务调用中。
- 不要在 UI 线程使用 `Dispatcher.Invoke`。

---

# AI 助手规则

AI 助手应当像一名长期参与项目的贡献者。

始终：

- 保留现有架构。
- 遵循现有编码风格。
- 扩展现有代码，而不是重写。
- 保持生成的代码可读。
- 保持生成的代码可维护。
- 优先选择简单方案。
- 结束前检查相关文件。
- 尊重现有设置、工具契约与 IPC 契约。

绝不：

- 重写整个项目。
- 重构无关代码。
- 引入不必要的依赖。
- 未经许可改变架构。
- 替换框架。
- 猜测项目需求。
- 悄悄改变模型行为或默认设置。

如果需求不明确：

先询问澄清，或留下 TODO 注释，而不是自行假设。

---

# 禁止行为

除非明确要求，绝不：

- 重写整个项目。
- 未经用户明确许可替换 UI 框架。
- 更换编程语言。
- 移除现有架构。
- 引入重量级第三方库。
- 升级 NuGet 包。
- 修改 `.csproj` 中的包版本。
- 硬编码配置。
- 硬编码资源。
- 硬编码 AI 服务商。
- 硬编码 API 密钥或密钥。
- 删除用户代码。
- 无理由修改公共接口。
- 破坏 IPC 协议或工具结构。
- 实现未来路线图中的功能。

---

# 版本策略

当前版本：

v0.1.6

目标：

在扩展现有系统的同时，保持已发布的桌宠稳定。

规则：

- 未来系统应通过版本迭代逐步引入。
- 除非被要求，不要实现未来路线图中的功能。
- 除非被要求，不要升级版本号。
- 保持对设置、记忆文件与角色包的向后兼容。

---

# 开发目标

第一优先级是稳定、可用的桌面伙伴。

每个功能都应改善：

- 稳定性
- 可读性
- 可维护性
- 可扩展性

长期可维护性比短期便利更重要。

---

# 协作规则

始终尊重用户最新的指令。

如果本文档与用户请求冲突，以用户请求为准。

不要替用户做架构决策。

当存在多种可行方案时：

选择最简单的一种。

必要时说明取舍。

避免不必要的复杂度。

让项目便于未来维护。

---

# AI 工作流程

1. 理解用户需求。
2. 分析现有架构与相关文件。
3. 做最小且合理的改动。
4. 保持兼容性（设置、记忆、角色包、IPC、工具结构）。
5. 复查修改后的代码。
6. 检查编译期与 XAML 问题。
7. 必要时更新相关文件与文档。
8. 说明改动内容。
9. 停止。

除非明确要求，不要实现未来功能。
