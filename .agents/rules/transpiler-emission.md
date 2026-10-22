# Transpiler Emission Rules (SourceWriter / Generated Output)

Mandatory rules for any code that emits C# (Source Generators) or CUDA C++ (`.cu`, e.g. `RoslynToCudaVisitor.cs`).

## 1. Absolute Prohibitions

1. **NO Reflection**: Never use `System.Reflection` during Source Generation or Transpilation. Use only `SemanticModel`, `ISymbol`, and `CSharpSyntaxWalker`.
2. **Zero Allocation**: Use `ref struct`, `Span<T>`, or the custom `SourceWriter`. Avoid temporary arrays and `List<T>`.
   - **Prefer interpolation straight into the writer**: `writer.Append($"{type} {name}")` (also `AppendLine`, `Line`, `AppendIf`, `AppendLineIf`, `LineIf`) goes through `SourceWriterInterpolatedStringHandler` and writes into the writer's buffer with zero allocation. Use it freely; splitting the same text into a chain of `Append` calls, or building it in a separate `StringBuilder`, is slower and harder to read.
   - Holes that are `string`, `char` or an integer type write without allocating. An enum, a symbol or any other type goes through the generic `AppendFormatted<T>` and boxes and calls `ToString()`; pass a string for it instead (`symbol.Name`, a constant). A `float` / `double` hole formats with the current culture; never use one for emitted numeric literals.
   - The zero-allocation path applies only when the `$""` is the argument of a writer method. `var s = $"..."`, `+` and `string.Format` allocate a string; use them only when a string value is actually needed (a dictionary key, a name stored in a model).

## 2. Scoped Brackets

Missing closers are a recurring bug source, so a bracket whose closer is written by a separate call must come from a scope helper.

- `{}`: always use a helper (`Scope()`, `Braces()`, `AppendEmptyScope()`). A brace never stays on one line.
- `()` / `[]`: use a helper when other calls (`Visit`, loops, several statements) emit the contents between the opener and the closer. When the whole bracketed text is written in one call, a literal is fine and reads better (`Append("(void*)")`, `Append("[0]")`).

- ❌ `_writer.Append("if ("); Visit(node.Condition); _writer.AppendLine(")");`
- ✅
```csharp
_writer.AppendKeyword("if");
using (_writer.Parentheses())
{
    Visit(node.Condition);
}
_writer.AppendLine();
```

### `SourceWriter` helpers

| Form | Helper | Output |
| ----- | ----- | ----- |
| Scoped (`using`) | `Scope()` | `{` `}` with line breaks / block structure |
| Scoped (`using`) | `Parentheses()` | `(` `)` inline |
| Scoped (`using`) | `Brackets()` | `[` `]` inline |
| Standalone | `AppendParentheses()` | `()` |
| Standalone | `AppendBrackets()` | `[]` |
| Standalone | `AppendEmptyScope()` | `{}` |

Use the standalone helpers instead of an empty `using` block. The rest of the `SourceWriter` API (separators, keywords,
conditional text, generated-file headers) and the name helpers are listed in
[helpers.md §3](../skills/gsharp-transpiler-guidelines/references/helpers.md#3-emitting-code).

## 3. Dense, Left-Aligned Output

1. **Zero indentation**: All generated lines (including `spc.AddSource` outputs) are fully left-aligned.
2. **No decorative spacing** around operators/symbols (`=`, `,`, `+`). Use `AppendComma()`, `AppendOperator()`, `AppendKeyword()`, etc. Never hardcode spaces in string literals.
