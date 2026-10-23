---
name: "GSharp Transpiler & Source Generator Guidelines"
description: "Guidelines and best practices for developing the GSharp C#-to-CUDA Transpiler and Roslyn Source Generators."
---

# Transpiler & Source Generator Workflow

G# reads C# via Roslyn Source Generators to emit C# boilerplate/stubs and transpile logic into CUDA C++ (`.cu`), building output with the custom `SourceWriter`.

This skill is the **procedure**. The binding **rules** live in `.agents/rules/`; read them before editing transpiler code:

| Concern | Rule file |
| ----- | ----- |
| NO Reflection / Zero Allocation, scoped brackets, dense left-aligned output | [transpiler-emission.md](../../rules/transpiler-emission.md) |
| `[GpuReadOnly]`, diagnostics, struct guards, ref/out, `var` suffix, design-time I/O, MSBuild wildcard | [generator-invariants.md](../../rules/generator-invariants.md) |
| `just` test categories, DiffEngine hang prevention | [test-execution.md](../../rules/test-execution.md) |
| TDD methodology, snapshot hierarchy | [test-architecture.md](../../rules/test-architecture.md) |
| C# naming, `!` ban, `StackLinq` / `System.Linq` policy | [coding-style.md](../../rules/coding-style.md) |
| Kernel / device function / launcher names | [gsharp-naming-conventions.md](../../rules/gsharp-naming-conventions.md) |
| Helper scripts and `dotnet run ... -- <mode>` tools | [agent-scripts.md](../../rules/agent-scripts.md) |
| Existing helpers to reuse (queries, emission, diagnostics) | [references/helpers.md](references/helpers.md) |

## Procedure: Adding a New Syntax / Operator / Transpiler Feature

1. **Test first.** Create the snapshot test and verify the expected CUDA C++ output *before* implementing emission.
   1. Add a `.cs` case under `tests/Compiler/Kassyi.GSharp.Tests.Shared/TestCases/`. `SnapshotTestCaseGenerator` collects it into `Test_GeneratorSnapshots` (`Generator/GeneratorSnapshotTests.cs`); no registration is needed. A case that exists only to drive an analyzer test is marked `[SkipSnapshotConsistency("...")]`.
   2. The generator does not re-run when only a file under `TestCases/` changes; touch any compiled `.cs` in the test project (or rebuild) so the new case is picked up.
   3. Run the relevant `just` command (see test-execution.md) and review the `.verified` snapshot.
2. **Branch by AST lane** to allow parallel work without interference: `Statement`, `Expression`, `Declaration`.
3. **Implement** using `SourceWriter` scopes per transpiler-emission.md, resolving symbols through `SemanticModel`.
   - **Before writing any query, check, or emission code, read [references/helpers.md](references/helpers.md)** and use the helper it lists instead of writing the logic again.
   - When you add, rename, or move a helper, update helpers.md in the same change and run `node .agents/skills/gsharp-transpiler-guidelines/scripts/check-helpers.js`.
4. **Report unsupported constructs** via diagnostics (generator-invariants.md §2), never silently.
5. **Re-run** the relevant test category.

## Symbol File Mapping & Demangling (`*.gsym.json`)

When G# emits physical AOT artifacts (`.g.cu` / `.g.cpp`), it emits a sidecar symbol file (`.g.cu.gsym.json` / `.g.cpp.gsym.json`) using JSON Lines:
- Line 1: `{"gsharp_symbols":"<hash>","dialect":"<dialect>"}`
- Entries: `{"cpp":"<identifier>","kind":"<kind>","cs":"<csName>","symbols":"<hash>","dialect":"<dialect>"}`

Line 1 of physical AOT source files contains `// gsharp-symbols: <hash>`. To demangle compiler logs or CUDA errors back to C# symbols, run `just gsharp-demangle <log> [symbols_path]`.

> [!TIP]
> The rules are not exhaustive. When unsure about supported syntax or design constraints, search `docs/spec/` before assuming.
