# Henicos (GSharp) Agent Context

This is the root index and behavioral charter for AI agents working in this repository. Detailed rules and specialized skills are delegated to `.agents/rules/` and `.agents/skills/`.

## 1. What is G#? (Core Manifest)

G# (GSharp) is a transpilation engine and runtime that compiles C# directly to CUDA C++/PTX using Roslyn Source Generators and runs natively on NVIDIA GPUs.

### The 3 Core Tenets
1. **AST-Driven**: All code analysis, transpilation, and safety enforcement are performed strictly via Roslyn Syntax Nodes and Semantic Models at compile-time (**NEVER use Reflection**: it slows the compilation down; see [transpiler-emission.md](.agents/rules/transpiler-emission.md) §1).
2. **Zero-Allocation**: GPU device execution fundamentally enforces zero reference-type heap allocations. Use value types (`struct`) and stack/register memory only.
3. **Static Determinism**: All kernel logic and types must be fully resolved at compile-time with zero runtime overhead—**NO Reflection (for compile speed, Tenet 1), NO `System.Linq` (in-house zero-allocation `StackLinq` / `Kassyi.ZeroAlloc.Linq` is permitted and encouraged), and NO Dynamic Dispatch**.

---

## 2. Core Agent Guardrails

These rules govern agent behavior and tool execution:

- **No Autonomous Git Commits or Pushes**: Strictly prohibited under any circumstances. When modifications are finished, stop and prompt the user to invoke `/commit.ja.prompt` (WIP/prototype) or `/commit.en.prompt` (PR). Commits must never be performed directly on `main` or `master`.
- **Builds and Tests Go Through the Job Queue**: The machine and its GPU are shared by every agent and worktree. Run every `dotnet build` / `test`, `msbuild`, `just` test, fuzz, mutate and benchmark recipe and long test script as `node scripts/human/job-queue.js run [--gpu] -- "<command>"` ([job-queue.md](.agents/rules/job-queue.md)); never run one directly.
- **Before Writing C#**: Read [coding-style.md](.agents/rules/coding-style.md) before writing or editing C#, a new file included. Never an empty `catch`, never `!` without a comment saying why it is safe.
- **Communication & Tone**: Keep your tone quiet, highly professional, concise, and dry—like a seasoned senior backend engineer. All explanations, thinking processes, and Q&A must be in Japanese.
- **Issue & PR Shorthand (`#<number>`)**: When referenced without qualification, autonomously inspect using `gh issue view <number>` or `gh pr view <number>` without asking for clarification.
- **Issue & PR Numbers Carry Their Title**: Never write an issue or PR number alone; add a short title, as in `#1078 (MSVC /O2 miscompile)`, in chat, handoffs, briefs, memory, PR bodies and commit messages, so a wrong number shows. Copy numbers from tool output, not from memory. Before publishing text with numbers (issue / PR bodies and comments, commits, branch names), run `node scripts/agent/check-issue-refs.js --text "<text>"` (or `--file`); a PreToolUse hook in Claude Code (`.claude/settings.json`) and Antigravity (`.agents/hooks.json`) runs it on those commands and denies a number that does not exist, or a closing keyword (`Fixes #n`) that names a PR or a closed issue. A brief or handoff that refers to issues starts with a `number | title` table.
- **Issue & PR Titles in Chat Are Japanese**: Titles on GitHub stay in English, for search and for readers outside Japan. When showing one in chat (the title next to a number, tables, summaries), translate it into Japanese; keep the number exactly as the tool output gives it. Text written to GitHub or the repository keeps the English title.
- **Bare URLs End with a Space or Line Break**: In chat, never put text directly after a bare URL; full-width characters such as `（` are taken into the link and break it. Write the number and title first and put the URL on its own line, or follow the URL with a half-width space.

---

## 3. Rules Index (`.agents/rules/`)

Only the rules marked *always loaded* are in your context from the start. Read every other rule before the work its line names, and follow it; the line says when. Antigravity lists those rules by their description, and `CLAUDE.md` lists them for Claude Code, which also loads a rule by itself when you read or edit a file its `paths` cover (`.claude/rules/`, copied from `.agents/rules/` by `just update-agents`). The frontmatter is checked by `node scripts/human/check-rule-frontmatter.js`.

- [Semantics Charter](.agents/rules/semantics-charter.md) (*always loaded*): which definition wins (C#, GPU, CPU), no implicit meaning without a marker, undefined behavior is an error. Every design and compiler change must follow it.
- [Job Queue](.agents/rules/job-queue.md) (*always loaded*): the short form of running builds and tests through `scripts/human/job-queue.js`. Read [Job Queue Reference](.agents/rules/job-queue-reference.md) before `--after`, moving, cancelling or pausing jobs, weights, memory budgets, or the hook's exact rules.
- [Coding Style & Memory Guidelines](.agents/rules/coding-style.md): before writing or editing any C#. No empty catch, no `!`, naming, zero allocation, the `StackLinq` policy (`RS0030`), kernel files.
- [GPU / C# Boundary Naming Conventions](.agents/rules/gsharp-naming-conventions.md): before naming a kernel, device function or launch wrapper.
- [Transpiler Emission Rules](.agents/rules/transpiler-emission.md): before changing code that emits C# or CUDA C++. No Reflection, `SourceWriter` scoped brackets, dense output, plan then emit.
- [Generator Pipeline Invariants](.agents/rules/generator-invariants.md): before changing the compiler under `src/Compiler`. Diagnostics, original spans, struct guards, ref/out, `var` suffix, design-time I/O, evaluation order. The symbol file (`*.gsym.json`) is described in the transpiler skill.
- [Test Architecture & TDD Methodology](.agents/rules/test-architecture.md): before writing or changing a test. Tiers, snapshots (`.verified.cs`), attributes and names, the test analyzers.
- [Test Execution (`just`)](.agents/rules/test-execution.md): before running tests, fuzzing or mutation testing, or acting on a pull request (CI, review labels, merging).
- [Agent Scripts](.agents/rules/agent-scripts.md): before writing or changing a script or a `justfile` recipe. Node built-ins only (no `.sh` / `.ps1`), compiler-owned logic in a `Kassyi.GSharp.Compiler.Generators` mode, the script catalog.
- [Issue Priority (Milestones)](.agents/rules/issue-milestones.md): before filing or triaging an issue, choosing a milestone, or picking what to work on next.
- [Label Colors](.agents/rules/issue-labels.md): before creating or recoloring a GitHub label.
- [Hard Problem Routing](.agents/rules/model-routing.md): before choosing a model for a subagent, using Fable, or meeting a math optimization or complexity problem to hand to the user.
---

## 4. Specialized Skills Index (`.agents/skills/`)

You **MUST** load and read the corresponding skill using `view_file` when performing related tasks:

- [Transpiler & Source Generator Guidelines](.agents/skills/gsharp-transpiler-guidelines/SKILL.md): Roslyn syntax rewriters, AST visitors, and generator pipelines.
- [QA & Test Coverage Guidelines](.agents/skills/qa-testing-guidelines/SKILL.md): Test scenario matrix (Happy/Boundary/Sad) and snapshot workflows.
- [Property Test Design](.agents/skills/property-test-design/SKILL.md): Numeric / algorithm tests (GpuMath, CPU vs GPU): define properties first, prove each check is reached and can fail (one-line mutant).
- [PR Review Pairing](.agents/skills/pr-review-pairing/SKILL.md): Reviewing open PRs with a person: plain walk-through per file, agent verifies logic and API, person judges structure, bugs verified before posting, comment drafts, `agent-reviewed` label after posting (merge and CI checked first), the author's fixes confirmed per item before the PR is called ready to merge.
- [Review Probing](.agents/skills/review-probing/SKILL.md): Finding the bugs nobody asked about by treating every claim as a hypothesis to falsify, reverse-grepping sibling paths, running 10-line external oracles, scaling dimensions, and executing throwaway probes before reporting findings.

### Symbol Mapping & Demangling Tools (`*.gsym.json`)
G# generates sidecar symbol map files (`*.gsym.json`) next to AOT artifacts (`.g.cu` / `.g.cpp`) in `obj/`. Both the AOT source (line 1 header `// gsharp-symbols: <hash>`) and symbol file record a matching FNV-1a 64 source hash. Run `just gsharp-demangle <log> [symbols_path]` or `dotnet run --project src/Compiler/Kassyi.GSharp.Compiler.Generators -- demangle <log> [symbols_path]` to translate mangled C++ identifiers back to C# symbols.
