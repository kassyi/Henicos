---
name: "GSharp Transpiler & Source Generator Guidelines"
description: "Guidelines and best practices for developing the GSharp C#-to-CUDA Transpiler and Roslyn Source Generators."
---

# Source Generation & Transpilation Best Practice

This project (G#, GSharp) operates by reading C# code via Roslyn Source Generators to generate C# boilerplate/stubs, and ultimately transpiling the logic into CUDA C++ (`.cu`) source code. Both generation steps heavily rely on building code strings using the custom `SourceWriter`.

## 🚨 ABSOLUTE CORE RULE: Zero Allocation & NO Reflection 🚨

When analyzing G# syntax, generating AST nodes, or building transpile output, you **MUST** strictly adhere to two absolute rules. Violating either of these is completely unacceptable and will severely break the transpiler architecture:

1. **NO Reflection**: **NEVER** use `System.Reflection` to analyze types, methods, attributes, or properties during Source Generation or Transpilation. You must exclusively rely on Roslyn's `SemanticModel`, `ISymbol` interfaces, and `CSharpSyntaxWalker`. Reflection is strictly prohibited.
2. **Yes Zero Allocation**: Always enforce zero-allocation patterns. Use `ref struct`s, `Span<T>`, or the custom `SourceWriter` instead of building temporary arrays, `List<T>`, or excessive string concatenations (e.g., `+`, `string.Format`, `$""` where a writer append suffices).

## Core Principle: Use Zero-Allocation Scopes for Brackets and Parentheses

When generating C# code or ultimately generating `.cu` C++ code (e.g., in `RoslynToCudaVisitor.cs`), **NEVER manually manage braces (`{}`), parentheses (`()`), or brackets (`[]`) with raw string appends.**

Manual management is a breeding ground for bugs (e.g., missing closing parentheses).

- ❌ **DO NOT** write:

```csharp
_writer.Append("if (");
Visit(node.Condition);
_writer.AppendLine(")");
```

- ✨ **DO** use the `using` pattern with `SourceWriter` inline scopes and helpers:

```csharp
_writer.AppendKeyword("if");
using (_writer.Parentheses())
{
    Visit(node.Condition);
}
_writer.AppendLine();
```

## Supported Harnesses (`SourceWriter` extensions):

### Scoped (with `using`)

- `using (_writer.Scope())` -> outputs `{` and `}` with line breaks and block structure.

### Empty / Standalone (no `using` needed)

For cases where you only need to output empty brackets or parentheses without any inner content, use the direct append helpers instead of an empty `using` block:

- `_writer.AppendParentheses()` -> outputs `()` inline.
- `_writer.AppendBrackets()` -> outputs `[]` inline.
- `_writer.AppendEmptyScope()` -> outputs `{}` inline.

This prevents mismatched scopes and eliminates the risk of missing closing characters.

## No Spacing Rule (Dense Formatting)

When generating C# code or ultimately `.cu` C++ code, **do not insert spaces around operators or symbols** (e.g., `=`, `,`, `+`). To prevent spacing errors, let the AI output dense code without manual visual formatting. Rely exclusively on `SourceWriter` helpers (`AppendComma()`, `AppendOperator()`, `AppendKeyword()`, etc.) instead of hardcoding spaces in string literals.

## Mandatory Snapshot Testing for New Features (Test-Driven AI Vibe Coding)

Whenever you implement support for a new C# syntax, operator, or Transpiler feature in G#, you **MUST strictly follow the Test-Driven AI Vibe Coding methodology**:

1. **Phase 1: Test First.** You must design and create a corresponding snapshot test to verify the generated CUDA C++ output **before** implementing the syntax generation.
2. **Phase 2: AST Lane Branching.** To support parallel implementation, branch off into isolated lanes based on the AST responsibility area (e.g., `Statement`, `Expression`, `Declaration`) so that your changes do not interfere with other ongoing work.

### Steps to create a Snapshot Test:

1. Create a new `.cs` test case file inside `tests/Kassyi.GSharp.Tests/TestCases/`.
2. Add the corresponding `[InlineData("YourTestCase.cs")]` to the `GeneratorSnapshots` method in `GEN-08_GeneratorSnapshotTests.cs`.
3. Run `just test` to generate and verify the `.cu` source code snapshot.

### Test Execution Categories (`just`)

The repository provides several `just` commands to run tests grouped by category. Use the appropriate command for the domain you are modifying:

- `just test`: Runs all normal tests (excluding Stress tests).
- `just test-syntax`: Runs syntax parsing and C# transpilation tests (`Category=Syntax`).
- `just test-execution`: Runs actual GPU kernel execution tests on hardware (`Category=Execution`).
- `just test-sadpath`: Runs diagnostics and error detection tests (`Category=SadPath|Diagnostics`).
- `just test-stress`: Runs high-load and concurrency tests (`Category=Stress`).
- `just test-runtime`: Runs JIT engine and transpiler infrastructure tests (`Category=Runtime|Transpiler`).
- `just test-unit`: Runs general unit tests (`Category=Unit`).
- `just test-alignment`: Runs memory alignment and struct layout tests (`Category=Alignment`).
- `just test-all`: Runs absolutely all tests.

> [!IMPORTANT]
> **Always use `just` commands (or set `DiffEngine_Disabled=true` / `CI=true`) when running tests.**
> Snapshot diffs using `Verify` will attempt to launch interactive GUI diff viewers (DiffEngine) by default on failure, causing commands to block indefinitely in headless/AI agent environments. `just` commands automatically disable this behavior.

## Implicit Rules & Constraints

The GSharp generator pipeline strictly enforces several architectural rules. You **MUST** abide by these when modifying the transpiler logic:

1. **`[Constant]` Signature Exclusion**:
   Parameters decorated with `[Constant]` exist exclusively for runtime macro injection (`#define`). They **MUST NOT** be included in the generated C++ `__global__ void` kernel signature, nor in the host-side array marshaling logic. Always filter them using `!WrapperEmitter.IsConstant(p)`.

2. **No Silent Failures (Strict Diagnostics)**:
   If the transpiler encounters unsupported C# syntax (e.g., `new`, `string`, `LINQ`, recursion), it **MUST NOT** silently ignore it, output empty strings, or throw raw exceptions. Instead, it must gracefully report the issue via `context.ReportDiagnostic` using the appropriate `GSharpDiagnostics` (e.g., `GSH0001`, `GSH0003`).

3. **C++ Header Guards for Structs**:
   When emitting C++ definitions for C# `structs` (e.g., in `StructTranspiler`), the output **MUST** be wrapped in a preprocessor include guard (e.g., `#ifndef __GSHARP_STRUCT_{structName}__`). This prevents "struct has already been defined" errors when multiple kernels sharing the same struct are JIT-compiled together.

4. **Deep Struct Marshaling Restrictions**:
   Reference-type arrays (`T[]`) inside structs are automatically converted into unmanaged `IntPtr` fields within native shadow structures (`__Native_Name`). The transpiler must treat these array fields as raw pointers on the device side, not managed objects.

5. **Semantic-Aware Pointer Cascading (`ref` / `out`)**:
   When transpiling variable references (especially passing `ref`/`out` arguments to other methods), you **MUST** query the `SemanticModel` to check if the symbol is _already_ a pointer or reference type in the CUDA context. This prevents emitting invalid double-address operators (e.g., `&(&val)`) and ensures that C# member access (`.`) on `ref` structs is correctly transformed into C++ pointer access (`->`).

6. **Strict `var` Initialization (Literal Suffixes)**:
   While C#'s `var` is transpiled directly to C++'s `auto`, the transpiler enforces strict literal type constraints (`ImplicitVarNeedsSuffixRule`). When implementing new expressions, never allow `var` to be initialized by an untyped bare integer literal (e.g., `var x = 0;`). The transpiler must enforce explicit suffixes (`0u`, `1L`) for integer literals to prevent signedness errors caused by NVCC's `auto` inference differing from Roslyn's. (Note: Literals containing a decimal point or exponent are currently exempt from this strict suffix requirement).

> [!TIP]
> **Consult the Specifications (`docs/spec`)**
> The rules listed above are not exhaustive. The GSharp framework has numerous design constraints and transpilation limitations (e.g., unsupported C# features). Whenever you are unsure about implementation details, supported syntax, or architectural decisions, **always search and consult the specification documents in the `docs/spec/` directory** before making assumptions.
