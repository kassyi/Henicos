# Compiler Helper Catalog

Read this before adding code to `src/Compiler/Kassyi.GSharp.Compiler`. The catalog maps an intent to the helper that
already implements it. If a row fits, use the helper; do not write the logic again. Paths are relative to the compiler
project.

Do not read the whole catalog. Either grep the folder `.agents/skills/gsharp-transpiler-guidelines/references/helpers/`
(from the repository root) for a word of your intent (a table row is one line, so a hit is the whole row), or pick the
file from the table below and read only that file.

| File | Look here for | Example intents |
| ----- | ----- | ----- |
| [symbols.md](helpers/symbols.md) | Questions about symbols and attributes: kernel or device function, G# attributes, removed `[Conditional]` calls, struct layout and shadow structs, `Gpu` macros and barriers, control flow graphs, uninitialized-read and effect queries | Is this a kernel? Does a symbol carry a G# attribute? Is this call a synchronization intrinsic? Does a struct need a shadow struct? |
| [semantic-model.md](helpers/semantic-model.md) | The semantic model and syntax tree, and lowering: symbol and type of a node, original-span locations, lowering annotations, the intermediate representation (IR) and its spill plan, `foreach` and `Gpu.ForUnroll` expansion, state machine flattening | Symbol of a node. Location for a diagnostic after lowering. Attach a lowering annotation. Spill an argument across a split |
| [emitting.md](helpers/emitting.md) | Writing generated code: `SourceWriter`, host launchers and their names, parameter transfer and marshaling, `[CudaImport]` modules, managed versions and resume locals, C++ names, literals, error sites, string constants | Conditional text. How a kernel parameter crosses to the device. C++ name of a user symbol. C++ literal of a constant. Name of a module |
| [transpiler-rules.md](helpers/transpiler-rules.md) | Transpiler rules and emission forms: handling a syntax kind or call, `goto` and switch sections, StackLinq fusion, `Gpu.SyncGrid` and `Gpu.SingleThread` bodies, null checks of arrays, struct conversion and padding, integer semantics, order of evaluation | Handle a new `SyntaxKind`. A read of a C# array that throws on null. Write the C# result of an integer operator. Keep the C# order of evaluation |
| [diagnostics.md](helpers/diagnostics.md) | Defining and reporting a diagnostic from an analyzer, the transpiler or a kernel validation rule; restrictions shared by the analyzer and the transpiler; barrier divergence | Define a diagnostic. Report from an analyzer. Decide a G# restriction. Is a barrier divergent |
| [generator-models.md](helpers/generator-models.md) | Models of the incremental generator pipeline: arrays in models, stable hashes, launch limits | An array field in a pipeline model. A stable hash across processes |

A method called on a receiver (an extension or instance method) is written with a leading dot: `.GetSymbol` means
`model.GetSymbol(node)`, `.AppendIf` means `writer.AppendIf(...)`. Static members are written `Type.Member`.
`scripts/check-helpers.js` checks both forms in every file of `helpers/` and reports a receiver method written as
`Type.Member`; run it after adding or renaming a helper.
Script conventions and the catalog of agent scripts are in [agent-scripts.md](../../../rules/agent-scripts.md).

## 7. Adding a helper

- Add one when the third copy of the same logic appears, or when one definition must stay consistent across analyzers and
  emitters (as `GpuExecutionSpace` and `GpuMacroClassifier` do). Two similar lines in one file are not a reason.
- Where it goes:
  - Roslyn-only queries with no G# meaning: `Extensions/`.
  - Used outside the transpiler (analyzers, generator, marshaling, models): at the project root, a named static class
    when it is one type (`GpuExecutionSpace`), a folder of its own when several types belong together (`ErrorSites/`,
    `Dialects/`: the dialect interface the whole compiler uses sits beside its implementation, not under `Transpiler/`).
  - Transpiler-only: the subfolder of its feature under `Transpiler/Helpers/` (`NullChecks/`, `Numeric/`, `Literals/`,
    `StackArrays/`, `Calls/`, `Locals/`, `ResumeLocals/`), with the namespace of that folder added to `GlobalUsings.cs`
    when the folder is new; `Transpiler/Helpers/` itself only for helpers used across features (`EvaluationOrder`). A
    type that only one helper uses goes in the folder of that helper.
  - Used by one rule only: next to that rule under `Transpiler/Rules/`, not in `Transpiler/Helpers/`.
  - Generator plumbing: `Helpers/`.
- Then add a row to this file in the same change.
