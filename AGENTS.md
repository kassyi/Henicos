# Henicos (GSharp) Agent Context

This is the root index and behavioral charter for AI agents working in this repository. Detailed rules and specialized skills are delegated to `.agents/rules/` and `.agents/skills/`.

## 1. What is G#? (Core Manifest)

G# (GSharp) is a transpilation engine and runtime that compiles C# directly to CUDA C++/PTX using Roslyn Source Generators and runs natively on NVIDIA GPUs.

### The 3 Core Tenets
1. **AST-Driven**: All code analysis, transpilation, and safety enforcement are performed strictly via Roslyn Syntax Nodes and Semantic Models at compile-time (**NEVER use Reflection**).
2. **Zero-Allocation**: GPU device execution fundamentally enforces zero reference-type heap allocations. Use value types (`struct`) and stack/register memory only.
3. **Static Determinism**: All kernel logic and types must be fully resolved at compile-time with zero runtime overhead—**NO Reflection, NO `System.Linq` (in-house zero-allocation `StackLinq` / `Kassyi.ZeroAlloc.Linq` is permitted and encouraged), and NO Dynamic Dispatch**.

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
- [Test Execution (`just`)](.agents/rules/test-execution.md): `just` test categories and DiffEngine hang prevention.
- [Transpiler Emission Rules](.agents/rules/transpiler-emission.md): NO Reflection, `SourceWriter` scoped brackets, dense left-aligned output.
- [Generator Pipeline Invariants](.agents/rules/generator-invariants.md): `[Constant]` exclusion, diagnostics, struct guards, ref/out, `var` suffix, design-time I/O guard, MSBuild wildcard, C# order of evaluation (`EvaluationOrder`). The symbol file (`*.gsym.json`) and source hash header are described in the transpiler skill (Symbol File Mapping).
- [Issue Priority (Milestones)](.agents/rules/issue-milestones.md): the current bugs-first policy, the milestones `M1`–`M5` and `Future`, how to assign an issue, the order to work in, and what agents may change without approval.
- [Hard Problem Routing](.agents/rules/model-routing.md): when to use Fable for the hardest design/verification work, and handing math optimization / complexity reduction to the user as a self-contained plain-text problem (no file attachments).
- [Semantics Charter](.agents/rules/semantics-charter.md): which definition wins (C#, GPU, CPU), no implicit meaning without a marker, undefined behavior is an error. Every design and compiler change must follow it.
- [Agent Scripts](.agents/rules/agent-scripts.md): agent-run scripts are dependency-free Node `.js` (no `.sh` / `.ps1`); compiler-owned logic goes in a `Kassyi.GSharp.Compiler.Generators` mode; script catalog.

---

## 4. Specialized Skills Index (`.agents/skills/`)

You **MUST** load and read the corresponding skill using `view_file` when performing related tasks:

- [Transpiler & Source Generator Guidelines](.agents/skills/gsharp-transpiler-guidelines/SKILL.md): Roslyn syntax rewriters, AST visitors, and generator pipelines.
- [QA & Test Coverage Guidelines](.agents/skills/qa-testing-guidelines/SKILL.md): Test scenario matrix (Happy/Boundary/Sad) and snapshot workflows.
- [Property Test Design](.agents/skills/property-test-design/SKILL.md): Numeric / algorithm tests (GpuMath, CPU vs GPU): define properties first, prove each check is reached and can fail (one-line mutant).
- [PR Review Pairing](.agents/skills/pr-review-pairing/SKILL.md): Reviewing open PRs with a person: plain walk-through per file, agent verifies logic and API, person judges structure, bugs verified before posting, comment drafts, `human-reviewed` label after posting, `full-ci` after the author's fixes are confirmed.

### Symbol Mapping & Demangling Tools (`*.gsym.json`)
G# generates sidecar symbol map files (`*.gsym.json`) next to AOT artifacts (`.g.cu` / `.g.cpp`) in `obj/`. Both the AOT source (line 1 header `// gsharp-symbols: <hash>`) and symbol file record a matching FNV-1a 64 source hash. Run `just gsharp-demangle <log> [symbols_path]` or `dotnet run --project src/Compiler/Kassyi.GSharp.Compiler.Generators -- demangle <log> [symbols_path]` to translate mangled C++ identifiers back to C# symbols.
