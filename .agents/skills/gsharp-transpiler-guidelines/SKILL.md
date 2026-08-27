---
name: "GSharp Transpiler & Source Generator Guidelines"
description: "Guidelines and best practices for developing the GSharp C#-to-CUDA Transpiler and Roslyn Source Generators."
---

# Source Generation & Transpilation Best Practices (GSharp)

This project (GSharp) operates by reading C# code via Roslyn Source Generators to generate C# boilerplate/stubs, and ultimately transpiling the logic into CUDA C++ (`.cu`) source code. Both generation steps heavily rely on building code strings using the custom `SourceWriter`.

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
- `using (_writer.Parentheses())` -> outputs `(` and `)` inline.
- `using (_writer.Brackets())` -> outputs `[` and `]` inline.

### Empty / Standalone (no `using` needed)
For cases where you only need to output empty brackets or parentheses without any inner content, use the direct append helpers instead of an empty `using` block:
- `_writer.AppendParentheses()` -> outputs `()` inline.
- `_writer.AppendBrackets()` -> outputs `[]` inline.
- `_writer.AppendEmptyScope()` -> outputs `{}` inline.

This prevents mismatched scopes and eliminates the risk of missing closing characters.

## No Spacing Rule (Dense Formatting)

When generating C# code or ultimately `.cu` C++ code, **do not insert spaces around operators or symbols** (e.g., `=`, `,`, `+`). To prevent spacing errors, let the AI output dense code without manual visual formatting. Rely exclusively on `SourceWriter` helpers (`AppendComma()`, `AppendOperator()`, `AppendKeyword()`, etc.) instead of hardcoding spaces in string literals.

## Mandatory Snapshot Testing for New Features

Whenever you implement support for a new C# syntax, operator, or Transpiler feature in G#, you **MUST** create a corresponding snapshot test to verify the generated CUDA C++ output.

1. Create a new `.cs` test case file inside `tests/Kassyi.GSharp.Tests/TestCases/`.
2. Add the corresponding `[InlineData("YourTestCase.cs")]` to the `GeneratorSnapshots` method in `GEN-08_GeneratorSnapshotTests.cs`.
3. Run `just test` to generate and verify the `.cu` source code snapshot.

> [!IMPORTANT]
> **Always use `just test` (or set `DiffEngine_Disabled=true` / `CI=true`) when running tests.**
> Snapshot diffs using `Verify` will attempt to launch interactive GUI diff viewers (DiffEngine) by default on failure, causing commands to block indefinitely in headless/AI agent environments. `just test` automatically disables this behavior.
