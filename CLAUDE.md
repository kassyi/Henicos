<!-- BEGIN Antigravity global AGENTS MANAGED -->
@AGENTS.md

Rules read on demand: read the file before the work its description names, and follow it.
(.claude/rules/ holds the rules loaded at start and those loaded with the files their paths cover.)
- .agents/rules/agent-scripts.md: Read before writing or changing a script under scripts/ or .agents/, or a justfile recipe - language, folders, conventions, and the catalog of scripts.
- .agents/rules/agent-tool-mapping.md: How the Antigravity tool names in workflows and skills map to Claude Code tools and when to delegate to subagents.
- .agents/rules/coding-style.md: Read before writing or editing any C# - no empty catch, no null-forgiving operator, naming, zero allocation, the StackLinq policy, kernel files.
- .agents/rules/generator-invariants.md: Read before changing the compiler under src/Compiler - diagnostics, original spans, struct guards, ref/out, design-time I/O, evaluation order.
- .agents/rules/gsharp-naming-conventions.md: Read before naming a kernel, a device function or a generated launch wrapper in C# - suffixes, no underscores or prefixes.
- .agents/rules/issue-labels.md: Read before creating or recoloring a GitHub label - three tiers by style, one hue family per label, the dark-mode check.
- .agents/rules/issue-milestones.md: Read before filing or triaging an issue, choosing a milestone, or picking what to work on next - milestones M1 to M5 and Future, how to assign one, the order of work.
- .agents/rules/job-queue-reference.md: Read before using the job queue beyond run, wait and status - ordering, --after dependencies, moves, pause, slots and memory budgets, GPU turns, scheduler restarts, the hook rules.
- .agents/rules/model-routing.md: Read before choosing a model for a subagent, using Fable, or meeting a math optimization or complexity problem to hand to the user.
- .agents/rules/test-architecture.md: Read before writing or changing a test - test tiers, snapshot rules, test attributes and names, the test analyzers, the APIs banned in tests.
- .agents/rules/test-execution.md: Read before running tests, fuzzing or mutation testing, or acting on a pull request (CI, review labels, merging) - just recipes, GPU fault tests, memory budgets, CI.
- .agents/rules/transpiler-emission.md: Read before changing code that emits C# or CUDA C++ - no reflection, SourceWriter and scoped brackets, dense output, plan then emit.
<!-- END Antigravity global AGENTS MANAGED -->
