# Generator Pipeline Invariants

Architectural constraints enforced by the GSharp generator pipeline. Abide by these when modifying transpiler/generator logic. For anything not listed, consult `docs/spec/` before assuming.

## 1. `[Constant]` Signature Exclusion
`[Constant]` parameters exist only for runtime macro injection (`#define`). Exclude them from the generated `__global__ void` signature and host-side array marshaling. Filter with `!WrapperEmitter.IsConstant(p)`.

## 2. No Silent Failures (Strict Diagnostics)
Unsupported C# syntax (`new`, `string`, LINQ, recursion, ...) must not be ignored, emitted as empty strings, or thrown as raw exceptions. Report via `context.ReportDiagnostic` with the matching `GSharpDiagnostics` (e.g. `GSH0001`, `GSH0003`).

## 3. C++ Header Guards for Structs
C++ definitions for C# `struct`s (e.g. `StructTranspiler`) must be wrapped in an include guard (`#ifndef __GSHARP_STRUCT_{structName}__`) to avoid redefinition when kernels sharing a struct are JIT-compiled together.

## 4. Deep Struct Marshaling
`T[]` fields inside structs become unmanaged `IntPtr` fields in native shadow structs (`__Native_Name`). On the device side treat them as raw pointers, not managed objects.

## 5. Semantic-Aware Pointer Cascading (`ref` / `out`)
When transpiling variable references (esp. `ref`/`out` arguments), query the `SemanticModel` to determine whether the symbol is already a pointer/reference in CUDA. This prevents `&(&val)` and ensures member access `.` on `ref` structs becomes `->`.

## 6. Strict `var` Initialization (`ImplicitVarNeedsSuffixRule`)
`var` maps to `auto`. Never allow `var` initialized from an untyped bare integer literal (`var x = 0;`); require explicit suffixes (`0u`, `1L`) to avoid NVCC/Roslyn signedness divergence. Literals with a decimal point or exponent are exempt.

## 7. Guard Physical File I/O Against IDE Design-Time Workers
When a generator writes AOT artifacts (`.g.cu` / `.g.cpp`, e.g. `GpuKernelGenerator.WriteUnityAotArtifact`) to `$(IntermediateOutputPath)`, skip the physical write when `IsDesignTimeCompilation(compilation)` is true.
- **Why**: IDE workers (`JetBrains.Roslyn.Worker`, `ServiceHub.RoslynCodeAnalysisService`, `LanguageServer`, `OmniSharp`) read `build_property.IntermediateOutputPath` and run generators on every change. They race with `csc.exe`/`nvcc`/`cl.exe`/`g++` and overwrite valid artifacts using stale in-memory `CompilationReference`s (`CandidateReason.Ambiguous`).
- **Pitfall**: A worker that loaded an older `Kassyi.GSharp.Compiler.dll` keeps overwriting `obj/` until restarted.

## 8. Unconditional Wildcard Exclusion for Pre-Compile Generated Sources
When an MSBuild target (`GenerateNativeApi`, `GenerateYamlDialects`, `GenerateDiagnostics`) emits multiple `.g.cs` files into `$(IntermediateOutputPath)` and includes a subset, always add:
```xml
<Compile Remove="**\GeneratedNativeApi\**\*.g.cs" />
```
Relying only on `$(IntermediateOutputPath)GeneratedNativeApi\**\*.g.cs` fails during IDE design-time evaluation (before `Sdk.targets` appends `$(TargetFramework)`) or when `BaseIntermediateOutputPath` changes (`obj/<os>/`), causing duplicate types across `Core`, `Runtime`, `Compiler`.
