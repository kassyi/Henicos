# Transpiler Emission Rules (SourceWriter / Generated Output)

Mandatory rules for any code that emits C# (Source Generators) or CUDA C++ (`.cu`, e.g. `RoslynToCudaVisitor.cs`).

## 1. Absolute Prohibitions

1. **NO Reflection**: Never use `System.Reflection` during Source Generation or Transpilation. Use only `SemanticModel`, `ISymbol`, and `CSharpSyntaxWalker`.
2. **Zero Allocation**: Use `ref struct`, `Span<T>`, or the custom `SourceWriter`. Avoid temporary arrays, `List<T>`, and string concatenation (`+`, `string.Format`, `$""`) where a writer append suffices.

## 2. Scoped Brackets (No Manual Braces/Parentheses)

Never manage `{}`, `()`, `[]` with raw string appends. Missing closers are a recurring bug source.

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

Use the standalone helpers instead of an empty `using` block.

## 3. Dense, Left-Aligned Output

1. **Zero indentation**: All generated lines (including `spc.AddSource` outputs) are fully left-aligned.
2. **No decorative spacing** around operators/symbols (`=`, `,`, `+`). Use `AppendComma()`, `AppendOperator()`, `AppendKeyword()`, etc. Never hardcode spaces in string literals.
