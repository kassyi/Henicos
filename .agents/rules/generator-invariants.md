# Generator Pipeline Invariants

Architectural constraints enforced by the GSharp generator pipeline. Abide by these when modifying transpiler/generator logic. For anything not listed, consult `docs/spec/` before assuming.

## 1. `[GpuConstexpr]` Parameters
`[GpuConstexpr]` parameters will become per-entry-point `constexpr` locals in A-10 Stage 2; until then they are passed as ordinary runtime arguments. Filter with `p.IsConstexprParameter()` (`SymbolExtensions`), not `HasAttribute(KnownAttributes.GpuConstexpr)` spelled out.

## 2. No Silent Failures (Strict Diagnostics)
Unsupported C# syntax (`new`, `string`, LINQ, recursion, ...) must not be ignored, emitted as empty strings, or thrown as raw exceptions. Report via `context.ReportDiagnostic` with the matching `GSharpDiagnostics` (e.g. `GSH0001`, `GSH0003`). How to define and report one from an analyzer, the transpiler or a kernel validation rule: [helpers.md §5](../skills/gsharp-transpiler-guidelines/references/helpers.md#5-diagnostics).
- **Lowered trees keep their original spans**: The C++ kernels and suspending device functions are transpiled from a lowered tree outside the compilation. `Reporter.Report` maps a node back to the user's source through the original-span annotation (`LoweringAnnotations.OriginalSpanAnnotationKind`) of the node or its nearest annotated ancestor (`GetReportLocation`). A lowering pass must keep that annotation on the nodes it copies or rebuilds. A node without one is reported with `Location.None`. A C++ build drops C++ diagnostics that the CUDA run already reported (same ID, same location), so a location-less one is lost whenever the CUDA run has the same ID without a location.
- **Copies carry their region in the same annotation**: When `IteratorInliner` (and later, the expansions of `PreLoweringExpander`) copies a subtree into the method, `LoweringAnnotations.StampRegion` writes the copy's region number into the fourth field of the original-span annotation (`"{start},{length},{treeOrdinal},{region}"`; read it with `LoweringAnnotations.TryParseOriginalSpanData` / `LoweringIdentity.GetRegion`). The rewrites of `ContextHoister` (which locals it hoists is still decided by name), the label states of `StateMachineLowerer` and the managed context field names key declarations by `DeclarationKey` (symbol and region); C++ names read the region from the node (`CppNameResolver`). Only the expansions stamp a region; every other pass copies the annotation as it is and never adds a second one.

## 3. C++ Header Guards for Structs
C++ definitions for C# `struct`s (e.g. `StructTranspiler`) must be wrapped in an include guard (`#ifndef __GSHARP_STRUCT_{structName}__`) to avoid redefinition when kernels sharing a struct are JIT-compiled together.

## 4. Deep Struct Marshaling
`T[]` fields inside structs become unmanaged `IntPtr` fields in native shadow structs (`__Native_Name`). On the device side treat them as raw pointers, not managed objects.

## 5. Semantic-Aware Pointer Cascading (`ref` / `out`)
When transpiling variable references (esp. `ref`/`out` arguments), query the `SemanticModel` to determine whether the symbol is already a pointer/reference in CUDA. This prevents `&(&val)` and ensures member access `.` on `ref` structs becomes `->`. `RefSyntaxHelper` implements this for arguments ([helpers.md §4](../skills/gsharp-transpiler-guidelines/references/helpers.md#4-transpiler-rules)).

## 6. Strict `var` Initialization (`ImplicitVarNeedsSuffixRule`)
`var` maps to `auto`. Never allow `var` initialized from an untyped bare integer literal (`var x = 0;`); require explicit suffixes (`0u`, `1L`) to avoid NVCC/Roslyn signedness divergence. Literals with a decimal point or exponent are exempt.

## 7. Guard Physical File I/O Against IDE Design-Time Workers
When a generator writes AOT artifacts (`.g.cu` / `.g.cpp`, written by `Aot/AotArtifactWriter.Write`) to `$(IntermediateOutputPath)`, skip the physical write when `IsDesignTimeCompilation(compilation)` is true.
- **Why**: IDE workers (`JetBrains.Roslyn.Worker`, `ServiceHub.RoslynCodeAnalysisService`, `LanguageServer`, `OmniSharp`) read `build_property.IntermediateOutputPath` and run generators on every change. They race with `csc.exe`/`nvcc`/`cl.exe`/`g++` and overwrite valid artifacts using stale in-memory `CompilationReference`s (`CandidateReason.Ambiguous`).
- **Pitfall**: A worker that loaded an older `Kassyi.GSharp.Compiler.dll` keeps overwriting `obj/` until restarted.
- **Stryker.NET**: Its process (`Stryker.CLI`, under `dotnet-stryker`) compiles mutated trees in process and runs the generator on them. `AotArtifactWriter.IsToolHost` treats it like an IDE worker, so a mutation run never replaces the real artifacts with mutated ones. Do not delete the artifacts around a run: when no C# input changed, the next build skips `CoreCompile`, the generator does not run, and the native build finds no `.cu` / `.cpp` to compile.

## 8. Unconditional Wildcard Exclusion for Pre-Compile Generated Sources
When an MSBuild target (`GenerateNativeApi`, `GenerateYamlDialects`, `GenerateDiagnostics`) emits multiple `.g.cs` files into `$(IntermediateOutputPath)` and includes a subset, always add:
```xml
<Compile Remove="**\GeneratedNativeApi\**\*.g.cs" />
```
Relying only on `$(IntermediateOutputPath)GeneratedNativeApi\**\*.g.cs` fails during IDE design-time evaluation (before `Sdk.targets` appends `$(TargetFramework)`) or when `BaseIntermediateOutputPath` changes (`obj/<os>/`), causing duplicate types across `Core`, `Runtime`, `Compiler`.

## 9. C# Order of Evaluation in Emitted C++ (`EvaluationOrder`)
C# evaluates operands, arguments and the target of an assignment from left to right; C++ leaves the order of call arguments and of most binary operands unspecified and evaluates the right side of an assignment first (C++17 `[expr.ass]`). Every emitted form that passes several operands in one C++ expression goes through `EvaluationOrder` (`Transpiler/Helpers/EvaluationOrder.cs`): calls (`EvaluationOrder.BeginCall`), binary operators, simple and compound assignments, operator macros. When two operands interfere (`ExpressionEffects.InterferesWith`, with callees classified by `DeviceFunctionEffects`), the form becomes an immediately invoked lambda that evaluates them into `__gsharp_seq_<n>` in the C# order. A new output form with several operands (arguments, operands, a target and a value) must use the helper; forms that C++17 already sequences (`E1[E2]`, `<<` / `>>`, `&&`, `||`, `?:`, `,`, brace initialization) do not need it. Lowering spills the arguments of a suspending call itself (`WarpInstructionRewriter.SpillArguments`), including an identifier that a later argument writes.
