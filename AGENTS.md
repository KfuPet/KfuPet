# AGENTS.md

# KfuPet AI Development Guidelines

Version: 3.0

> Languages: **English** | [简体中文](docs/AGENTS.zh-CN.md)

> If this document conflicts with explicit user instructions, always follow the user's latest instructions.

This document defines the development rules that every AI coding agent must follow when contributing to the KfuPet project.

The goal is to keep the project architecture clean, maintainable, and consistent throughout its development.

---

# Project Information

Project Name

KfuPet

Project Type

AI-powered anime desktop companion (desktop pet) platform.

Current Version

v0.1.6 (Beta, released with installer and updater)

Target Platform

Windows 10 / 11 (64-bit)

Framework

WPF (.NET 8.0)

Language

C#

IDE

Visual Studio 2022

Future Platforms

Android (independent project)

Desktop and Android are completely separate projects.

Do NOT attempt to merge them into one project unless explicitly requested.

---

# Current Implemented Systems

The following systems already exist in the codebase:

- Transparent, borderless, always-on-top desktop window
- Character rendering with a fixed humanoid skeleton and sprite attachments
- Character packages loaded dynamically from `Characters/`
- Mouse interaction: dragging, boundary snap-back, touch reactions
- AI conversation through OpenAI-compatible APIs (configurable models)
- Local model inference with GGUF files through LLamaSharp
- Model provider switching (multiple configured models, one active)
- Memory system: short-term, archive, and long-term memory
- Emotion system with post-conversation judging and touch reactions
- Proactive chat based on idle state and foreground application
- Tool calling (function calling) with a tool registry
- Character gallery, chat history, and long-term memory windows
- Tray menu, settings window, light/dark theme resources
- Update checking (GitHub → Gitee fallback) and an external updater
- Startup announcement and log service
- Developer mode and Named Pipe debugging IPC for the external developer tool

These systems are the current baseline. Extend them instead of replacing them.

---

# Future Systems

The following items are planned but NOT implemented. Do NOT implement them unless explicitly requested:

- Voice / TTS
- Skeleton animation runtime (the `Animation` / `Keyframe` models exist but are unused)
- Expression system (the `Expression` models exist but are unused)
- Plugin system
- Character repository
- AI super resolution
- Developer SDK
- Character editor
- Performance monitor
- Click-through mode and adaptive window sizing
- Vision service (`VisionService` is a stub)
- Android debugging HTTP server

`docs/reference/备忘录.md` tracks these stubs. Update it when a stub becomes a real implementation.

---

# Development Philosophy

Working Code First.

Architecture Second.

Optimization Third.

Always prefer:

Working code

over

Perfect code.

Avoid over-engineering.

Keep implementations simple.

---

# General Principles

Priority:

1. Correctness

2. Readability

3. Maintainability

4. Performance

Never sacrifice readability for unnecessary optimization.

---

# Coding Style

Always:

- Use PascalCase for classes.
- Use PascalCase for public members.
- Use camelCase for local variables.
- Prefix private fields with "_".
- Use async/await whenever appropriate.
- Add XML documentation for public APIs.
- Keep methods short and focused.

Never:

- Duplicate code.
- Use magic numbers.
- Hardcode resources.
- Hardcode AI providers.
- Hardcode configuration values.
- Block the UI thread.

---

# Project Structure

The repository is a single WPF project at the repository root:

```
KfuPet.slnx                  Solution file
KfuPet.csproj                Single project, RootNamespace = KfuPet
App.xaml / App.xaml.cs       Application startup and lifetime
MainWindow.xaml / .cs        Desktop pet window
SplashWindow.xaml / .cs      Splash screen
GlobalUsings.cs              Project-wide global usings

Assets/                      Icons and static UI images
Characters/                  Built-in character packages (each in its own folder)
Controls/                    Custom WPF controls (e.g. CharacterCanvas)
Core/
    Math/                    Matrix3x3 and math primitives
    Memory/                  Memory entries and stores
    Rendering/               RenderContext, Renderer, SkeletonRenderer
Helpers/                     MarkdownRenderer, ModelReplyText
Models/                      Data models (character, skeleton, AI, settings, updates)
Properties/                  Assembly and project metadata
Resources/                   XAML resource dictionaries (colors, typography, dialogs)
Services/
    Commands/                IPC command models
    Ipc/                     Named Pipe servers and clients
    Tools/                   ITool implementations and ToolRegistry
    ...                      Business services
Views/                       Windows and dialogs (XAML + code-behind)
docs/                        Project documentation
```

Runtime data is NOT part of the repository. It lives under the cache root:

```
%AppData%\KfuPet\            Default cache root
    Config\                  settings.json, models.json, stopwords.json, deployed-models.json
    Memory\Characters\<角色文件夹>\  Short-term/archive memory and emotion state (per character)
    Memory\LongMemory.json   Shared long-term memory
    Logs\                    KfuPet-*.log
    Models\                  Downloaded GGUF files
```

The cache root can be changed by the user; it is stored in `HKCU\Software\KfuPet` (`CacheRoot`).

Never create a `Config/` folder inside the repository. Use `AppPaths` to resolve runtime paths.

Do NOT split the project into multiple projects unless explicitly requested.

---

# Architecture

Separate responsibilities.

UI layer (`Views/`, `Controls/`):

Responsible only for:

- Rendering
- Windows and dialogs
- User interaction
- Animation and visual state playback

Business logic (`Services/`):

Responsible for:

- AI and model calls
- Character behavior
- Memory
- Emotion
- Tools
- Configuration
- Updates and logging

Core (`Core/`, `Models/`):

- Shared data structures
- Math, memory storage, and rendering primitives

Rules:

- Prefer MVVM, but existing view code-behind may handle pure view concerns.
- Never put business logic in XAML code-behind. Move it to a service.
- Avoid circular dependencies between services. Shared state belongs in `SettingsService` or a dedicated service.
- Business logic must live in services. View code-behind may orchestrate service calls (as `MainWindow` does), but it must not contain provider, memory, tool, or configuration logic.

---

# Character System

Character resources (images, animations, expressions, metadata, prompts) must be loaded dynamically.

Never hardcode character resources or character-specific paths.

A character package is a folder under `Characters/<CharacterId>/`:

- `character.json` — id, name, version, author, description, preview, tags, prompt entry
- `prompt.md` — character prompt and persona
- `reactions.json` — touch / interaction reactions
- `attachments.json` — sprite attachments bound to skeleton bones

The exact format is defined in `docs/guides/角色包制作指南.md`. Follow that document instead of inventing new fields.

The character skeleton (bone hierarchy) is fixed for this project — only human characters are supported — so the skeleton definition may be hardcoded in code.

The debug bone overlay (`ShowDebugBones`) is a permanent creator tool for aligning the model against the skeleton reference. It is off by default and toggled from the settings / developer tool. Do NOT treat it as temporary debug code to be removed.

Adding or changing character package fields requires updating the character package guide and the loader together.

---

# Skeleton and Rendering

Rendering uses native WPF (`DrawingContext`, `Canvas`, `Shape`) and cached bitmap attachments.

- `Core/Rendering/RenderContext` owns the drawing surface.
- `Core/Rendering/SkeletonRenderer` draws bones, attachments, and the debug overlay.
- `Controls/CharacterCanvas` exposes `Skeleton` and `ShowDebugBones` dependency/CLR properties.

Rules:

- Do not replace the rendering stack with SkiaSharp, Direct2D, or a game engine without explicit permission.
- Keep alpha hit-testing and bitmap caches intact; they prevent per-frame decoding.
- Do not add per-frame allocations in the render path.
- Rendering must stay on the UI thread; heavy image decoding or file IO must be offloaded.

---

# Animation

The animation runtime is NOT implemented yet.

Current state:

- `Models/Animation.cs`, `Models/Keyframe.cs`, `Models/AnimationState.cs`, and `Models/Expression.cs` exist but are unused placeholders.
- Do not build a skeleton animation player, expression system, or physics system unless explicitly requested.

When animation is implemented, it must support:

- Skeleton
- Sprite Attachments
- Animation Clips
- Expressions
- Physics

Keep the existing model types usable as a starting point.

---

# AI Providers

AI Providers must always be replaceable.

Remote providers use an OpenAI-compatible `/chat/completions` endpoint.

Rules:

- Never hardcode a provider, endpoint, model name, or API key.
- All remote calls go through `ChatService` and the active `ModelConfig` from `ModelConfigService`.
- A new provider must be expressible as `BaseUrl` + `ApiKey` + `ModelId`; do not add provider-specific branches unless the provider truly requires them.
- API keys are encrypted with DPAPI through `ProtectedStringConverter`. Never bypass it, never log keys, and never write keys to plain text.
- Tool calls use the OpenAI-compatible `tools` schema. Do not invent a second tool-calling protocol.
- Every new AI call site must declare a `ModelCallKind` and a `TokenUsageCategory` so usage tracking and user settings keep working.
- Non-main-chat call sites must respect `SettingsService.GetModelSource(kind)`; do not read settings and hardcode `Local` or `Remote`.

The main conversation always uses the active remote model. Local models are opt-in per call site only.

---

# Local Models

Local inference uses LLamaSharp with GGUF weights.

Approved packages (do NOT change versions or swap runtimes without permission):

- `LLamaSharp` 0.27.0
- `LLamaSharp.Backend.Cpu` 0.27.0
- `LLamaSharp.Backend.Vulkan.Windows` 0.27.0 (do NOT use the `LLamaSharp.Backend.Vulkan` meta package — it also pulls the Linux natives into the Windows package)

Key files:

- `Models/InferenceDevice.cs` — `Auto` / `Gpu` / `Cpu`
- `Models/HardwareInfo.cs` — hardware summary for recommendations
- `Models/LocalModelOption.cs` — downloadable model catalog entry
- `Models/DeployedModel.cs` — downloaded model record
- `Services/HardwareProbe.cs` — CPU / RAM / dedicated VRAM detection
- `Services/LocalModelCatalog.cs` — built-in model catalog and recommendation rules
- `Services/LocalModelDownloadService.cs` — download with mirror fallback and SHA256
- `Services/LocalModelDeploymentService.cs` — `deployed-models.json` and file management
- `Services/LocalModelRuntime.cs` — LLamaSharp runtime
- `Services/LocalModelService.cs` — enable / disable / load / release coordination
- `Services/ChatService.cs` — local-first inference with remote fallback

Runtime rules:

- Only one local model may be loaded at a time, and only one inference may run at a time (`LocalModelRuntime` gate).
- Context size is 8192. Do not change it without measuring memory impact on the 8B model.
- GPU layer count must be resolved through `LocalModelRuntime.ResolveGpuLayerCount`. Do not hardcode layer counts elsewhere.
- `Auto` uses GPU only when dedicated VRAM is at least 1.2 × the model file size; otherwise it falls back to CPU.
- Model loading must never run on the UI thread. Loading is done in background tasks.
- Release the model on disable, switch, delete, and application exit. Never leave a loaded model without an active deployment record.
- Download URLs must keep the mirror-first order with an official fallback; do not remove the fallback URL.

Calling rules:

- Local inference is only allowed for call sites that declare a `ModelCallKind`.
- A local call must fall back to the remote model when: no model is active, the model lacks the capability, the runtime is not loaded, a short-reply call finds the runtime busy, inference times out, or inference throws.
- Never surface a local inference failure as a user-facing error; log it and fall back.
- `Qwen3-1.7B` does not support memory extraction or memory summarization. Keep `SupportsMemoryExtraction` / `SupportsMemorySummarization` accurate.
- Emotion judging (`ModelCallKind.EmotionJudge`) must not be saved as `Local`; it is not stability-tested on local models.
- When adding a local model or changing its capabilities, update `docs/reference/模型API调用点清单.md`.

---

# Memory System

Memory is one of the project's core systems.

Current implementation:

- `Core/Memory/` — memory entries and stores
- `Services/MemorySystem.cs` — extraction, scoring, summarization, archive flow
- `Services/MemoryManager.cs` — high-level memory operations
- `Services/MemoryTriggerService.cs` — trigger words
- `Services/StopWordsService.cs` — retrieval stop words
- Files are stored under `%AppData%\KfuPet\Memory` (or the custom cache root)

Memory tiers:

- Short-term memory
- Archive memory
- Long-term memory

Rules:

- Keep memory storage independent from AI providers.
- Do not couple memory with emotion.
- Do not change the on-disk memory format without migration handling.
- Memory extraction must respect local model capability flags.
- New memory calls must pass the correct `ModelCallKind` and `TokenUsageCategory`.
- Memory operations must not block the UI thread.

Future support (not implemented):

- Searchable / vector memory
- Memory importance tuning UI
- Memory expiration

Do not implement these unless requested.

---

# Emotion System

Emotion should remain independent.

Current implementation:

- `Services/EmotionService.cs` — emotion state and decay
- `Services/EmotionJudgeService.cs` — post-conversation judging
- `Models/EmotionState.cs`

Emotion affects:

- Animation
- Expressions
- Dialogue
- Behavior
- Voice (future)

Rules:

- Do not tightly couple Emotion with Memory.
- Emotion changes must go through `EmotionService`; do not mutate emotion state directly from views.
- Emotion judging is remote-only for now.
- Local model changes must not silently alter emotion behavior.

---

# Tools and Function Calling

Tools are defined in `Services/Tools/` and registered in `ToolRegistry`.

Currently registered tools:

- `WebSearchTool`
- `BalanceTool`
- `EmotionTool`
- `FeedTool`
- `MemorySearchTool`
- `OpenAppTool`
- `ListRunningAppsTool`

Rules:

- A new tool must implement `ITool` and be registered in the main window's tool registry.
- Tool names and parameter schemas are part of the model-facing contract. Do not rename them without updating prompts and documentation.
- Never perform destructive or privileged actions from a tool without explicit user confirmation.
- `OpenAppTool` must respect the allowed-app list; full access requires the existing confirmation flow.
- Do not add hidden tools that are not filtered by user settings.
- Tools must not block the UI thread and must handle cancellation where applicable.
- `WebSearchTool` reuses the active model's provider key and calls the provider's official search API (DeepSeek or Kimi `/v1/tools/search`). It must return the provider's real results or an explicit failure; never fabricate results.

---

# IPC and Developer Mode

IPC exists for the external developer tool (`KfuPet-Tool`).

Implementation:

- `Services/Ipc/NamedPipeServer.cs` — `KfuPet.Skeleton` command pipe
- `Services/Ipc/LogPipeServer.cs` / `LogPipeClient.cs` — `KfuPet.Log` log stream
- `Services/CommandDispatcher.cs` — routes commands by `service` field
- `Services/DeveloperModeService.cs` — developer mode switch
- `docs/skeleton/SKELETON_API.md` — protocol contract

Rules:

- Do not break the existing pipe names, command names, or payload shapes.
- Any protocol change must update `docs/skeleton/SKELETON_API.md` in the same change.
- Command handlers must validate input and return errors instead of throwing into the pipe loop.
- Developer mode must remain off by default.

---

# Window

Current implementation:

- Transparent
- Borderless
- Always on top
- Draggable with boundary snap-back
- Tray menu

Future (not implemented):

- Adaptive window size
- Click-through mode
- Window animation

Only implement requested features.

---

# Configuration

Avoid hardcoded configuration.

Current configuration is JSON under the cache root:

- `Config\settings.json` — user settings
- `Config\models.json` — remote model configurations
- `Config\stopwords.json` — retrieval stop words
- `Config\deployed-models.json` — downloaded local models

Rules:

- Resolve paths through `AppPaths`; never hardcode `%AppData%` paths.
- Read and write settings through `SettingsService`; do not open `settings.json` directly from views.
- Do not add a new config file without a migration and a default path.
- Configuration changes must be backward compatible or migrate old values.
- Never commit runtime configuration or secrets.

---

# Assets

Load resources dynamically whenever possible.

Never hardcode asset paths.

Asset locations:

- `Assets/` — icons and static UI images
- `Resources/` — XAML resource dictionaries (colors, typography, dialog styles)
- `Characters/` — character package assets

Rules:

- Use resource dictionaries for theme colors and styles; do not hardcode colors in views.
- Character images belong in the character package, not in `Assets/`.
- Do not rename or move existing assets without updating all references.

---

# Performance

Avoid:

- Blocking UI thread
- Infinite polling
- Unnecessary allocations
- Frequent object creation
- Parallel local inference

Prefer:

- Async programming
- Lazy loading
- Caching

Rules:

- Local model loading and inference must run in background tasks.
- Do not run AI requests on the UI thread.
- Do not add timers that poll continuously; use events or scheduled one-shot timers.
- Keep the render path allocation-free where practical.
- Do not optimize prematurely.

---

# Security

Never:

- Hardcode API keys
- Hardcode secrets
- Store passwords in source code
- Log API keys, prompts containing secrets, or full request bodies

Always:

- Load secrets from configuration.
- Keep API keys DPAPI-encrypted through `ProtectedStringConverter`.
- Filter user- or model-provided links to `http` / `https` before opening them.
- Validate file paths before opening or deleting files.
- Treat tool arguments from the model as untrusted input.

---

# Third-party Libraries

Currently approved dependency:

- LLamaSharp (CPU + Vulkan backends) for local GGUF inference

Everything else should use the .NET 8 / WPF built-in stack.

Rules:

- Do not add SkiaSharp, NAudio, SQLite, CommunityToolkit.Mvvm, or other heavy dependencies without explicit permission.
- Do not upgrade NuGet packages without permission.
- Do not add Preview or Experimental packages.
- Do not modify `.csproj` package versions without permission.

---

# Global Usings

`ImplicitUsings` is disabled in `KfuPet.csproj`.

`GlobalUsings.cs` provides only:

- `System`
- `System.Collections.Generic`
- `System.IO`
- `System.Linq`
- `System.Net.Http`
- `System.Threading`
- `System.Threading.Tasks`

Rules:

- Add explicit `using` statements for anything else.
- `UseWindowsForms` is enabled, so `System.Windows.Forms` and `System.Drawing` can collide with WPF types. Fully qualify or alias when needed.
- Do not enable `ImplicitUsings` without permission.

---

# Git Rules

Keep commits focused.

Do not modify unrelated files.

Do not rename files unless requested.

Do not delete existing code unless requested.

Respect the current project architecture.

Commit message format (from `CONTRIBUTING.md`):

```
type：summary
```

- Use a full-width colon `：`.
- Types: `feat`, `fix`, `docs`, `chore`.
- One commit does one thing. Do not mix unrelated changes.
- Never commit `bin/`, `obj/`, runtime `Config/`, logs, model files, or secrets.

---

# Documentation

When adding new systems:

- Update `README.md` if user-facing behavior changes.
- Update `CONTRIBUTING.md` if the development workflow or structure changes.
- Add XML comments for public APIs.
- Keep documentation synchronized with implementation.

Relevant documents:

- `docs/guides/角色包制作指南.md` — character package format
- `docs/skeleton/SKELETON_API.md` — IPC / skeleton protocol
- `docs/skeleton/SKELETON_BONES.md` — skeleton bone reference
- `docs/reference/模型API调用点清单.md` — model call sites and local model capability matrix
- `docs/design/情感系统与DPI缩放.md` — emotion and DPI design notes
- `docs/reference/备忘录.md` — stubs and unused models

Rules:

- Changing behavior described by one of these documents requires updating that document in the same change.
- Do not create new documentation files for one-off notes; extend the existing documents.

---

# Code Validation

After every code modification:

The AI agent must check for obvious code issues before finishing the task.

Always:

- Check for syntax errors.
- Check for missing using statements (remember `ImplicitUsings` is disabled).
- Check for undefined variables.
- Check for undefined methods.
- Check for incorrect class or namespace references.
- Check for mismatched method signatures.
- Check for XAML name / event handler mismatches.
- Check for resource keys that do not exist.
- Check for common C# compiler errors that can be detected statically.

If any errors are found:

The AI should immediately fix them before completing the task.

Do not intentionally leave known compile-time errors.

The AI is NOT required to build the project.

The user will build and run the project inside Visual Studio.

When modifying existing code, always check whether the changes require updates to other related files.

For example:

- Namespace changes
- Constructor parameter changes
- Interface changes
- Method signature changes
- Class renaming
- Configuration changes
- Settings schema changes
- Character package format changes
- IPC protocol changes

Do not modify only one file if related files also require updates.

Before completing a task, always review the modified code for diagnostics.

If any compile-time diagnostics are visible, fix them before finishing.

Do not intentionally leave code with known errors.

Never tell the user to compile the project before checking the modified code yourself.

Always perform a self-review of the generated code before considering the task complete.

---

# WPF Guidelines

Prefer native WPF capabilities whenever possible.

Use:

- `AllowsTransparency`
- `WindowStyle=None`
- `Background=Transparent`
- Native WPF shapes, `DrawingContext`, and resource dictionaries

Prefer MVVM.

Keep UI and business logic separated.

Avoid unnecessary Win32 API calls unless required. Registry reads for hardware probing and cache-root settings are accepted exceptions; justify any new interop.

Use `Dispatcher` correctly:

- UI updates on the UI thread.
- Background work on `Task.Run` or async service calls.
- Never use `Dispatcher.Invoke` from the UI thread.

---

# AI Agent Rules

The AI assistant should behave like a long-term project contributor.

Always:

- Preserve existing architecture.
- Follow existing coding style.
- Extend existing code instead of rewriting it.
- Keep generated code readable.
- Keep generated code maintainable.
- Prefer simple solutions.
- Check related files before finishing.
- Respect existing settings, tool contracts, and IPC contracts.

Never:

- Rewrite the project.
- Refactor unrelated code.
- Introduce unnecessary dependencies.
- Change architecture without permission.
- Replace frameworks.
- Guess project requirements.
- Silently change model behavior or default settings.

If requirements are unclear:

Ask for clarification or leave a TODO comment instead of making assumptions.

---

# Forbidden Actions

Unless explicitly requested, NEVER:

- Rewrite the entire project.
- Replace the UI framework without explicit user permission.
- Change programming language.
- Remove existing architecture.
- Add heavy third-party libraries.
- Upgrade NuGet packages.
- Modify `.csproj` package versions.
- Hardcode configuration.
- Hardcode resources.
- Hardcode AI providers.
- Hardcode API keys or secrets.
- Delete user code.
- Modify public interfaces without reason.
- Break the IPC protocol or tool schemas.
- Implement future roadmap features.

---

# Version Strategy

Current version:

v0.1.6

Goal:

Keep the released desktop pet stable while extending existing systems.

Rules:

- Future systems should be introduced gradually through version updates.
- Do not implement future roadmap features unless requested.
- Do not bump the version number unless requested.
- Keep backward compatibility for settings, memory files, and character packages.

---

# Development Goal

The first priority is a stable, working desktop companion.

Every feature should improve:

- Stability
- Readability
- Maintainability
- Extensibility

Long-term maintainability is more important than short-term convenience.

---

# Collaboration Rules

Always respect the user's latest instructions.

If this document conflicts with the user's request, the user's request takes priority.

Do not make architectural decisions on behalf of the user.

When multiple valid solutions exist:

Choose the simplest one.

Explain trade-offs when necessary.

Avoid unnecessary complexity.

Keep the project approachable for future maintenance.

---

# AI Working Process

1. Understand the user's request.
2. Analyze the existing architecture and related files.
3. Make the smallest reasonable change.
4. Preserve compatibility (settings, memory, character packages, IPC, tool schemas).
5. Review modified code.
6. Check for compile-time and XAML issues.
7. Update related files and documentation if necessary.
8. Explain the changes.
9. Stop.

Do not implement future features unless explicitly requested.
