# Henicos (GSharp) Agent Context

This is the root index and behavioral charter for AI agents working in this repository. Detailed rules and specialized skills are delegated to `.agents/rules/` and `.agents/skills/`.

## 1. What is G#? (Core Manifest)

G# (GSharp) is a transpilation engine and runtime that compiles C# directly to CUDA C++/PTX using Roslyn Source Generators and runs natively on NVIDIA GPUs.

### The 3 Core Tenets
1. **AST-Driven**: All code analysis, transpilation, and safety enforcement are performed strictly via Roslyn Syntax Nodes and Semantic Models at compile-time (**NEVER use Reflection**).
2. **Zero-Allocation**: GPU device execution fundamentally enforces zero reference-type heap allocations. Use value types (`struct`) and stack/register memory only.
3. **Static Determinism**: All kernel logic and types must be fully resolved at compile-time with zero runtime overhead—**NO Reflection, NO LINQ, and NO Dynamic Dispatch**.

---

## 2. Core Agent Guardrails

These rules govern agent behavior and tool execution:

- **No Autonomous Git Commits or Pushes**: Strictly prohibited under any circumstances. When modifications are finished, stop and prompt the user to invoke `/commit.ja.prompt` (WIP/prototype) or `/commit.en.prompt` (PR). Commits must never be performed directly on `main` or `master`.
- **Communication & Tone**: Keep your tone quiet, highly professional, concise, and dry—like a seasoned senior backend engineer. All explanations, thinking processes, and Q&A must be in Japanese.
- **Issue & PR Shorthand (`#<number>`)**: When referenced without qualification, autonomously inspect using `gh issue view <number>` or `gh pr view <number>` without asking for clarification.

---

## 3. Rules Index (`.agents/rules/`)

You **MUST** follow the dedicated rule files for code implementation:

- [Coding Style & Memory Guidelines](.agents/rules/coding-style.md): C# naming conventions, zero-allocation patterns, `StackLinq` policy (`RS0030`), null-forgiving (`!`) ban, and empty catch ban.
- [GPU / C# Boundary Naming Conventions](.agents/rules/gsharp-naming-conventions.md): Kernel and host naming standards.
- [Test Architecture & TDD Methodology](.agents/rules/test-architecture.md): 2-Tier test hierarchy, snapshot testing (`.verified.cs`), and Test-Driven AI Vibe Coding workflow.

---

## 4. Specialized Skills Index (`.agents/skills/`)

You **MUST** load and read the corresponding skill using `view_file` when performing related tasks:

- [Transpiler & Source Generator Guidelines](.agents/skills/gsharp-transpiler-guidelines/SKILL.md): Roslyn syntax rewriters, AST visitors, and generator pipelines.
- [QA & Test Coverage Guidelines](.agents/skills/qa-testing-guidelines/SKILL.md): Test scenario matrix (Happy/Boundary/Sad) and snapshot workflows.
- [Roadmap & Task Management](.agents/skills/roadmap-management/SKILL.md): Updating project roadmaps, tracking technical debt, and progress telemetry.
