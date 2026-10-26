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

- `{}` with contents: always use a helper (`Scope()`, `Braces()`). An empty `{}` is the literal `Append("{}")`.
- `()` / `[]`: use a helper when other calls (`Visit`, loops, several statements) emit the contents between the opener and the closer. When the whole bracketed text is written in one call, a literal is fine and reads better (`Append("(void*)")`, `Append("[0]")`).

- ❌ `_writer.Append("if ("); Visit(node.Condition); _writer.AppendLine(")");`
- ✅
```csharp
_writer.Append("if");
using (_writer.Parentheses())
{
    Visit(node.Condition);
}
_writer.AppendLine();
```

### `SourceWriter` scope helpers

| Helper | Output |
| ----- | ----- |
| `Scope()` | `{` `}` with line breaks / block structure |
| `Braces()` | `{` `}` inline (initializer lists) |
| `Parentheses()` | `(` `)` inline |
| `Brackets()` | `[` `]` inline |
| `ImmediatelyInvokedLambda()` | `([&]() {` ... `})()` |

To keep the C# order of evaluation, an emitted form whose operands interfere is wrapped by `EvaluationOrder`
(`BeginCall` for calls, `BeginSequence` with `DeclareTemp` for operators and assignments); see
[generator-invariants.md §9](generator-invariants.md#9-c-order-of-evaluation-in-emitted-c-evaluationorder). Do not
open a lambda by hand for that purpose.

Fixed text without a separately written closer is a literal: `Append("()")`, `Append(",")`, `Append("if")`,
`AppendLine(";")`. `SourceWriter` has no per-token methods (`AppendComma`, `AppendKeyword`, ...); they only hid the text.
Conditional text, generated-file headers and the name helpers are listed in
[helpers.md §3](../skills/gsharp-transpiler-guidelines/references/helpers.md#3-emitting-code).

## 3. Dense, Left-Aligned Output

1. **Zero indentation**: All generated lines (including `spc.AddSource` outputs) are fully left-aligned.
2. **No decorative spacing**: do not add spaces, alignment or blank lines only for looks. Expression operators are written
   as they are (`Append(op)`, giving `a+b`); declarations keep `int x = 1;`. Do not respace existing output: the byte
   savings are negligible and every snapshot would change.

## 4. Building Syntax Trees (`SyntaxFactory`)

Rewriters (`Lowering/`, `ManagedCSharpTranspiler`) build C# with nested `SyntaxFactory` calls, which are unreadable without help.

1. **Shape comment**: Above every tree of more than a few calls, write the C# it produces on one line, with placeholders in `<>` (e.g. `// if (!__gsharp_resume(<call>, <frame index>)) { __gsharp_state = <n>; return; }`). Helpers that build a fixed shape carry it above the declaration.
2. **One argument per line**: If a call does not fit on one line, put each argument on its own line, one level deeper, and close the parenthesis on its own line aligned with the start of the call. Never stack closers (`))))`).
3. **Name the inner parts**: When nesting goes past about three levels, pull inner nodes into locals (`var label = ...;`) or small helpers (`CreateStateAssignment`) instead of nesting further.

```csharp
// <target> = <value>;
private static ExpressionStatementSyntax Assign(IdentifierNameSyntax target, ExpressionSyntax value) =>
    SyntaxFactory.ExpressionStatement(
        SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, target, value)
    );
```
