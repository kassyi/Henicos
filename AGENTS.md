# Henicos (GSharp) Agent Context

This file gives agents enough repository context to work safely and follow project-specific conventions.

<!-- BEGIN GENERATED PORTFOLIO CONTEXT -->

## Generated Repository Context

### Repository Metadata

- Primary language: C#
- Topics: csharp, source-generator, cuda, ptx, transpiler, gpu

### Working Summary

Henicos (GSharp) is a framework that allows executing C# code directly on NVIDIA GPUs. It transpiles C# code into CUDA C++ / PTX using Roslyn Source Generators and provides a Runtime (`GpuKernelLoader`, `BackendCompiler`) to compile and launch kernels natively.

<!-- END GENERATED PORTFOLIO CONTEXT -->

## What is G# (GSharp)?

"G#" (GSharp) is the internal name for this custom framework . **It is not an officially existing programming language.** Rather, it is a C#-to-CUDA transpilation engine and runtime built entirely within this repository. It enables C# developers to write GPU kernels in C# syntax, which are then intercepted by Roslyn Source Generators and translated into `.cu` (CUDA C++) code. When working on this project, treat "G#" as the domain-specific transpiler defined by this repository, not as a general-purpose language.

### G# Core Manifest: The 3 Tenets

> **"Bringing C#'s existing mental model directly into the GPU realm delivers the ultimate developer experience (DX)."**

1. **AST (Roslyn Syntax Nodes & Semantic Models)**: All code analysis, transpilation, and safety enforcement are performed strictly via Roslyn Abstract Syntax Trees and Semantic Models at compile-time.
2. **Zero-Allocation**: GPU device-side execution fundamentally enforces a zero-allocation guarantee—prohibiting dynamic reference-type heap allocations while fully leveraging value types (`struct`) and stack/register memory.
3. **Static Determinism**: All kernel logic and types must be fully resolved at compile-time with zero runtime overhead—**NO Reflection, NO LINQ, and NO Dynamic Dispatch**.

## 1. CRITICAL GUARDRAILS & PROHIBITED ACTIONS

These rules are absolute. Any violation will directly induce runtime failures, Git history pollution, or development workflow disruption.

### 1.1. Absolute Ban on Unauthorized Git Commits / Pushes

- **DO NOT execute `git commit` or `git push` automatically under any circumstances.** AI agents are prone to executing commits autonomously to "complete" a task, but this is strictly forbidden.
- When you finish modifying code, **STOP**. Do not commit. Instead, notify the user that modifications are complete and prompt them to invoke one of the commit workflows:
    - **For prototyping/WIP:** Suggest `/commit.ja.prompt` (to propose commits with Japanese messages).
    - **For publishing/PR creation:** Suggest `/commit.en.prompt` (to propose commits with English conventional messages).
- Only execute commits when running the explicitly invoked `/commit.*.prompt` workflow AND after the user replies with "yes" to your proposed commit plan.
- **DO NOT commit directly to 'main' or 'master' branch under any circumstances.** Commits must always be performed on a dedicated topic branch (e.g., `feat/...`, `refactor/...`, `fix/...`). If you are on `main` or `master`, you must create and switch to a topic branch first before proposing or executing commits.

### 1.2. Source Generator Constraints

- **NEVER use Reflection under any circumstances for transpilation.** All C# code transpilation to CUDA must be done via Roslyn Syntax Nodes (`CSharpSyntaxWalker`) and Semantic Models.

### 1.3. No Empty Catch Blocks (Exception Swallowing)

- **NEVER suppress or swallow exceptions** with an empty `catch { }` block unless accompanied by a clear English comment explaining _why_ it is safe to ignore (e.g., `// Ignore cleanup errors on best-effort basis`).

---

## 2. C# DEVELOPMENT & CODE STYLE CHARTER

For all C# coding style conventions (Naming, Syntax, Zero-Allocation preferences, etc.), you **MUST** strictly follow the dedicated rules files. Do not duplicate those rules here.

- **General C# Style:** [`.agents/rules/coding-style.md`](.agents/rules/coding-style.md)
- **GPU / C# Boundary Naming Conventions:** [`.agents/rules/gsharp-naming-conventions.md`](.agents/rules/gsharp-naming-conventions.md)
- **Test Architecture & Tier 1-3 Strategy:** [`.agents/rules/test-architecture.md`](.agents/rules/test-architecture.md)

---

## 3. PROJECT-SPECIFIC SKILLS

To keep this context file lean, specialized guidelines are offloaded to external skills. **You MUST load and read the corresponding skill using `view_file` when performing related tasks.**

- **Transpiler & Source Generator Development:**
  When modifying `Kassyi.GSharp.Compiler` or related Source Generation / Transpiler logic, you **MUST** read:
  [`.agents/skills/gsharp-transpiler-guidelines/SKILL.md`](.agents/skills/gsharp-transpiler-guidelines/SKILL.md)

- **QA & Test Coverage Guidelines:**
  When writing or modifying unit tests (xUnit), you **MUST** adhere to:
  [`.agents/skills/qa-testing-guidelines/SKILL.md`](.agents/skills/qa-testing-guidelines/SKILL.md)

- **Roadmap & Task Management:**
  When completing a task, fixing a tracked bug, or discovering new technical debt, you **MUST** read:
  [`.agents/skills/roadmap-management/SKILL.md`](.agents/skills/roadmap-management/SKILL.md)

---

## 4. DEVELOPMENT METHODOLOGY (Test-Driven AI Vibe Coding)

This project strictly adheres to **Test-Driven AI Vibe Coding (テスト先行AIバイブコーディング)**.
When picking up tasks from the roadmap, you MUST follow this fundamental workflow:

1. **Phase 1: Golden Master / Snapshot Test Generation First**
    - Before writing ANY implementation code, you must create the necessary test cases.
    - You MUST design the tests covering happy paths, boundaries, and sad paths (exceptions) based on the `qa-testing-guidelines` **before** writing the actual logic.
2. **Phase 2: Isolated AST Lane Branching**
    - Work in isolated branches separated by the AST responsibility area (e.g., Expression, Statement) to prevent parallel implementation conflicts.

---

## 5. OUTPUT VALIDATION & RESPONSE STYLE RULE

### 5.1. Static Code Analysis Validation

- Always validate that unused members are properly handled. In library components (`Kassyi.GSharp.Core`), use `#pragma warning disable IDE0051` or `// ReSharper disable UnusedMember.Global` when members are strictly intended as public API.

### 5.2. Tone & Communication Rules

- **Language:** All Q&A, explanations, and thinking must be in Japanese.
- **Professional Demeanor:** Keep your tone quiet, highly professional, concise, and dry—like a seasoned senior backend engineer. Focus entirely on logic, architecture, and safety.

### 5.3. Issue & PR Shorthand Resolution (`#<number>`)

- When the user references `#<number>` (e.g., `#214`, `#217`) without explicitly specifying "Issue" or "PR", **DO NOT ask for clarification**. Autonomously inspect and resolve it using the GitHub CLI (`gh`).
- Because GitHub shares the same numeric ID space for Issues and Pull Requests, immediately inspect `gh issue view <number>` (or `gh pr view <number>`) to retrieve the entity context and proceed with the task seamlessly.
