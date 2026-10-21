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

## Procedure: Adding a New Syntax / Operator / Transpiler Feature

1. **Test first.** Create the snapshot test and verify the expected CUDA C++ output *before* implementing emission.
   1. Add a `.cs` case under `tests/Compiler/Kassyi.GSharp.Tests.Shared/TestCases/`.
   2. Add `[InlineData("YourTestCase.cs")]` to `GeneratorSnapshots` in `GEN-08_GeneratorSnapshotTests.cs`.
   3. Run the relevant `just` command (see test-execution.md) and review the `.verified` snapshot.
2. **Branch by AST lane** to allow parallel work without interference: `Statement`, `Expression`, `Declaration`.
3. **Implement** using `SourceWriter` scopes per transpiler-emission.md, resolving symbols through `SemanticModel`.
4. **Report unsupported constructs** via diagnostics (generator-invariants.md §2), never silently.
5. **Re-run** the relevant test category.

> [!TIP]
> The rules are not exhaustive. When unsure about supported syntax or design constraints, search `docs/spec/` before assuming.
