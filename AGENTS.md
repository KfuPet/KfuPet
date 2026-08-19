# AGENTS.md

# KfuPet AI Development Guidelines

Version: 2.0

> If this document conflicts with explicit user instructions, always follow the user's latest instructions.

This document defines the development rules that every AI coding agent must follow when contributing to the KfuPet project.

The goal is to keep the project architecture clean, maintainable, and consistent throughout its development.

---

# Project Information

Project Name

KfuPet

Project Type

AI-powered anime desktop pet platform.

Current Development Stage

Prototype (v0.0.1)

Target Platform

Windows Desktop

Framework

WPF (.NET)

Language

C#

IDE

Visual Studio 2022

Future Platforms

Android (Independent Project)

Desktop and Android are completely separate projects.

Do NOT attempt to merge them into one project unless explicitly requested.

---

# Project Vision

KfuPet is designed to become an open AI desktop companion platform.

Long-term planned systems include:

- AI Conversation
- AI Provider Switching
- Character Package
- Skeleton Animation
- Memory System
- Emotion System
- Plugin System
- Developer Mode
- Character Repository
- AI Super Resolution
- Developer SDK
- Character Editor
- Performance Monitor

These systems are future plans.

Do NOT implement them unless requested.

---

# Current Milestone

Current milestone only focuses on:

- Transparent Window
- Borderless Window
- Character Rendering
- Basic Animation
- Mouse Interaction
- Stable Architecture

Everything else should wait until later versions.

---

# Development Philosophy

Prototype First.

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

Current project:

KfuPet.Desktop

Future project layout:

src/

    KfuPet.Desktop

    KfuPet.Core

    KfuPet.AI

    KfuPet.Render

    KfuPet.Animation

    KfuPet.SDK

    KfuPet.Studio

Do NOT split the project unless explicitly requested.

Inside the current project, organize files using folders.

Recommended folders:

Assets/

Views/

Models/

Services/

Helpers/

Controls/

Animations/

Resources/

Config/

---

# Architecture

Separate responsibilities.

UI layer:

Responsible only for:

- Rendering
- Windows
- User interaction
- Animation playback

Business logic:

Responsible for:

- AI
- Character behavior
- Memory
- Emotion
- Configuration

Avoid placing business logic inside XAML code-behind.

---

# Character System

Character resources (images, animations, expressions, voice, metadata) must be loaded dynamically.

Never hardcode character resources.

The character skeleton (bone hierarchy) is fixed for this project — only human characters are supported — so the skeleton definition may be hardcoded in code.

Future character package contains:

- Images
- Animations
- Metadata
- Configuration
- Voice
- Expressions

Future support:

Skeleton Animation

Design for extensibility.

---

# Animation

Future animation system should support:

- Skeleton
- Sprite Attachments
- Animation Clips
- Expressions
- Physics

Current prototype only requires simple animation.

Avoid implementing unnecessary complexity.

---

# AI Providers

AI Providers must always be replaceable.

Possible providers:

- OpenAI
- DeepSeek
- Gemini
- Claude
- Ollama
- Local Model

Design provider-independent code.

Never tightly couple the project to one provider.

---

# Memory System

Memory is one of the project's core systems.

Future design should support:

- Short-term Memory
- Long-term Memory
- Searchable Memory
- Vector Memory
- Memory Importance
- Memory Expiration

Memory should remain independent from AI Providers.

---

# Emotion System

Emotion should remain independent.

Emotion affects:

- Animation
- Expressions
- Dialogue
- Behavior
- Voice

Do not tightly couple Emotion with Memory.

---

# Window

Current prototype:

- Transparent
- Borderless
- Desktop Window

Future:

- Adaptive Window Size
- Click-through Mode
- Always On Top
- Window Animation

Only implement requested features.

---

# Assets

Load resources dynamically whenever possible.

Never hardcode asset paths.

Future assets include:

- Characters
- Audio
- Effects
- Fonts
- UI Resources

---

# Configuration

Avoid hardcoded configuration.

Future configuration may use:

- JSON
- YAML
- Character Package Config
- User Settings

---

# Performance

Avoid:

- Blocking UI thread
- Infinite polling
- Unnecessary allocations
- Frequent object creation

Prefer:

- Async programming
- Lazy loading
- Caching

Do not optimize prematurely.

---

# Security

Never:

- Hardcode API Keys
- Hardcode Secrets
- Store passwords in source code

Always load secrets from configuration.

---

# Git Rules

Keep commits focused.

Do not modify unrelated files.

Do not rename files unless requested.

Do not delete existing code unless requested.

Respect the current project architecture.

---

# Documentation

When adding new systems:

- Update README if necessary.
- Add XML comments for public APIs.
- Keep documentation synchronized with implementation.

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

Never:

- Rewrite the project.
- Refactor unrelated code.
- Introduce unnecessary dependencies.
- Change architecture without permission.
- Replace frameworks.
- Guess project requirements.

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
- Hardcode configuration.
- Hardcode resources.
- Hardcode AI providers.
- Delete user code.
- Modify public interfaces without reason.

---

# Version Strategy

Current version:

v0.0.1

Goal:

Create a stable prototype.

Future systems should be introduced gradually through version updates.

Do not implement future roadmap features unless requested.

---

# Development Goal

The first priority is to build a working desktop pet.

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
# Code Validation

After every code modification:

The AI agent must check for obvious code issues before finishing the task.

Always:

- Check for syntax errors.
- Check for missing using statements.
- Check for undefined variables.
- Check for undefined methods.
- Check for incorrect class or namespace references.
- Check for mismatched method signatures.
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

Do not modify only one file if related files also require updates
Before completing a task, always review the modified code for diagnostics.

If any compile-time diagnostics are visible, fix them before finishing.

Do not intentionally leave code with known errors.
Never tell the user to compile the project before checking the modified code yourself.

Always perform a self-review of the generated code before considering the task complete..

---
# WPF Guidelines

Prefer native WPF capabilities whenever possible.

Use:
- AllowsTransparency
- WindowStyle=None
- Background=Transparent

Prefer MVVM.
Keep UI and business logic separated.
Avoid unnecessary Win32 API calls unless required.

---
# Third-party Libraries

Preferred libraries:
- SkiaSharp
- NAudio
- SQLite
- CommunityToolkit.Mvvm

Avoid adding unnecessary dependencies.

---
# AI Working Process

1. Understand the user's request.
2. Analyze the existing architecture.
3. Make the smallest reasonable change.
4. Preserve compatibility.
5. Review modified code.
6. Check for compile-time issues.
7. Update related files if necessary.
8. Explain the changes.
9. Stop.

Do not implement future features unless explicitly requested.

---
# Additional Restrictions

Never:
- Upgrade NuGet packages without permission.
- Modify .csproj unless requested.
- Add Preview or Experimental packages.
- Change package versions without permission.
