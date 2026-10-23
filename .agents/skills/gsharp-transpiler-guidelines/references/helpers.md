# Compiler Helper Catalog

Read this before adding code to `src/Compiler/Kassyi.GSharp.Compiler`. Each row maps an intent to the helper that already
implements it. If a row fits, use the helper; do not write the logic again. Paths are relative to the compiler project.

Every `Type.Member` written in backticks below is checked by `scripts/check-helpers.js`; run it after renaming a helper.
Script conventions and the catalog of agent scripts are in [agent-scripts.md](../../../rules/agent-scripts.md).

## 1. Symbols and attributes

| Intent | Use | Do not write |
| ----- | ----- | ----- |
| Is this a kernel or a device function? | `GpuExecutionSpace.IsGpuFunction` | `HasAttribute(GpuKernel) \|\| HasAttribute(GpuDeviceFunction)` |
| Is this position inside device code (lambdas and local functions included)? | `GpuExecutionSpace.IsWithinGpuFunction` on `GetEnclosingSymbol(...)` | a hand-written `ContainingSymbol` loop |
| Does a symbol carry a G# attribute? | `SymbolExtensions.HasAttribute` with a `KnownAttributes` constant | `AttributeClass?.Name == "GpuKernelAttribute"` |
| Get the attribute data | `SymbolExtensions.GetAttribute` | `GetAttributes()` plus a loop |
| Compare symbols | `SymbolExtensions.IsEqualTo` | `SymbolEqualityComparer.Default.Equals` spelled out |
| Is the namespace `Kassyi.GSharp.Core` / `System` / `System.Numerics`? | `SymbolExtensions.IsCoreNamespace`, `SymbolExtensions.IsSystemNamespace`, `SymbolExtensions.IsSystemOrNumerics` | `ToDisplayString() == "System"` |
| Element type of `T[]`, `Span<T>`, `Memory<T>`, `ImmutableArray<T>` ... | `SymbolExtensions.TryGetArrayLikeElementType` | a switch over type names |
| Is it `SharedMemory<T>` / `System.Numerics.Vector*`? | `SymbolExtensions.IsSharedMemoryType`, `SymbolExtensions.IsSystemNumericsVector` | `Name == "SharedMemory"` |
| `[StructLayout(Pack = n)]` | `SymbolExtensions.TryGetStructLayoutPack` | reading `NamedArguments` by hand |
| First declaring syntax of a symbol | `SymbolExtensions.TryGetDeclaringSyntax` | `DeclaringSyntaxReferences[0].GetSyntax()` |
| Which `Gpu` macro is this call (`SyncGrid`, `SingleThread`, `ForUnroll` ...)? | `GpuMacroClassifier.Classify` | `symbol.Name == "SyncGrid"` |

Type and namespace names: `TypeConstants` (`TypeConstants.GpuType`, `TypeConstants.GpuMacros`). Attribute short names:
`KnownAttributes`. Never spell `"Gpu"`, `"SharedMemory"` or an attribute name as a literal.

## 2. Semantic model and syntax

| Intent | Use | Do not write |
| ----- | ----- | ----- |
| Symbol of a node | `SemanticModelExtensions.GetSymbol` | `GetSymbolInfo(node).Symbol` |
| Type of an expression | `SemanticModelExtensions.GetTypeSymbol` | `GetTypeInfo(node).Type` |
| Declared symbol of a given kind | `SemanticModelExtensions.GetDeclaredSymbol` (`GetDeclaredSymbol<IMethodSymbol>(decl)`) | `GetDeclaredSymbol(decl) as IMethodSymbol` |
| Constant value / operation that may not resolve | `SemanticModelExtensions.GetSafeConstantValue`, `SemanticModelExtensions.GetSafeOperation` | try/catch around Roslyn calls |
| Location in the user's source after lowering rewrote the tree | `SemanticModelExtensions.GetOriginalLocation` | `node.GetLocation()` on a rewritten node |
| Attach or read a lowering annotation (original span, suspending call, state machine `return`) | `LoweringAnnotations.OriginalSpanAnnotationKind`, `LoweringAnnotations.SuspendingCallAnnotationKind`, `LoweringAnnotations.StateMachineReturnAnnotationKind` | `node.HasAnnotations("StateMachineReturn")` |
| Method body, block or `=>` | `MethodDeclarationSyntaxExtensions.GetBodyNode`, `MethodDeclarationSyntaxExtensions.HasBody` | `Body ?? (SyntaxNode?)ExpressionBody` |
| Node or token is one of 2-3 kinds | `SyntaxExtensions.IsKind` (`node.IsKind(a, b)`) | `IsKind(a) \|\| IsKind(b)` |
| Walk a tree on a hot path | derive from `PooledWalker` and rent it (see `GpuKernelValidationWalker`) | `new MyWalker()` per call |

## 3. Emitting code

The binding rules are in `.agents/rules/transpiler-emission.md`. The helpers:

| Intent | Use | Do not write |
| ----- | ----- | ----- |
| `{ }` block with line breaks (always) | `SourceWriter.Scope` | `Append("{")` ... `Append("}")` |
| `{ }` inline (initializer lists) | `SourceWriter.Braces` | raw brace strings |
| `( )` / `[ ]` whose contents are emitted by other calls (`Visit`, loops, several statements) | `SourceWriter.Parentheses`, `SourceWriter.Brackets` | `Append("(")` ... `Visit(...)` ... `Append(")")` |
| `( )` / `[ ]` written whole in one call | a literal is fine: `Append("(void*)")`, `Append("[0]")` | — |
| Empty `()` / `[]` / `{}` | `SourceWriter.AppendParentheses`, `SourceWriter.AppendBrackets`, `SourceWriter.AppendEmptyScope` | an empty `using` block |
| Text with values in it | `$""` passed straight to `SourceWriter.Append` / `SourceWriter.AppendLine` / `SourceWriter.Line` (zero allocation via `SourceWriterInterpolatedStringHandler`) | a chain of `Append` calls for one line, a separate `StringBuilder`, or `var s = $"..."` then `Append(s)` |
| Separators and keywords | `SourceWriter.AppendComma`, `SourceWriter.AppendOperator`, `SourceWriter.AppendKeyword`, `SourceWriter.AppendSemiColonLine` | `Append(", ")`, `Append(" = ")` |
| Conditional text | `SourceWriter.AppendIf`, `SourceWriter.AppendLineIf` | `if (x) writer.Append(...)` |
| C# type name in generated host code | `CSharpTypeNameWriter.AppendCsharpTypeName`, `CSharpTypeNameWriter.AppendFullyQualifiedCsharpName` | `ToDisplayString()` |
| User identifier in generated host C# | `CSharpIdentifierExtensions.ToCSharpIdentifier` | prefixing `@` by hand |
| C++ name of a user symbol (declaration / reference) | `CppNameResolver.AppendDeclaredName`, `CppNameResolver.AppendReferencedName`, `CppNameResolver.AppendSymbolName` | `identifier.ValueText` written directly |
| Compiler-internal name (`__gsharp_*`) | `GSharpInternalNames` (add new names to `Resources/InternalNames.yml`) | a `"__gsharp_..."` literal |
| Header and attributes of a generated `.g.cs` | `SourceWriter.AppendGeneratedFileHeader`, `SourceWriter.AppendGeneratedCodeAttributes` | copied header text |

Where an internal name lives: in the YAML when the Prelude, the runtime, another emitter, or both host and device code
spell it; otherwise a `private const` in the one emitter that declares and uses it. See the remarks on `GSharpInternalNames`.
The C# names of kernels, device functions and generated launchers follow
[gsharp-naming-conventions.md](../../../rules/gsharp-naming-conventions.md).

## 4. Transpiler rules

A rule is stateless and gets everything through `ref TranspileContext context`:

- `context.Emitter.Writer`: the output (`SourceWriter`).
- `context.Query.SemanticModel`: symbol lookups.
- `context.Reporter.Report(descriptor, node, args)`: diagnostics.
- `context.Visitor.Visit(child)`: transpile a child node.

| Intent | Use |
| ----- | ----- |
| Handle a new `SyntaxKind` | derive from `SyntaxNodeTranspilerRuleBase` and register it in `TranspilerRuleRegistry` |
| Handle a call to a specific method | implement `IInvocationTranspilerRule` and add it to `TranspilerRuleRegistry.InvocationRules` (order matters) |
| Handle a `Gpu.*` statement macro | implement `IMacroTranspilerRule` and add it to `TranspilerRuleRegistry.MacroRules` |
| Map a BCL method to a CUDA / C++ function | the dialect YAML under `Dialects/`, not a new rule |
| Pass a `ref` / `out` argument | `RefSyntaxHelper.TranspileAsByRefArgument` |
| Transpile a lambda body inline | `LambdaTranspilerHelper.TranspileLambdaBody` |

The smallest example is `Transpiler/Rules/ExpressionRules/ParenthesizedExpressionTranspilerRule.cs`.

## 5. Diagnostics

| Intent | Use | Do not write |
| ----- | ----- | ----- |
| Define a diagnostic | an entry in `Diagnostics/diagnostics.json` (generates `GSharpDiagnostics`) | `new DiagnosticDescriptor(...)` |
| Report from an analyzer | `DiagnosticExtensions.ReportDiagnostic` (`context.ReportDiagnostic(descriptor, node, args)`) | `Diagnostic.Create(...)` plus `context.ReportDiagnostic` |
| Report from the transpiler | `context.Reporter.Report` | throwing, or emitting nothing |
| Check a syntax rule inside kernels | implement `ISyntaxValidationRule` and register it in `DiagnosticRuleRegistry` | a new analyzer that walks kernel bodies again |
| Diagnostic stored in an incremental pipeline model | `EquatableDiagnosticExtensions.AsEquatable` | a raw `Diagnostic` field |

## 6. Incremental generator models

| Intent | Use | Do not write |
| ----- | ----- | ----- |
| Array field in a pipeline model | `EquatableArray` (`EquatableArrayExtensions.AsEquatableArray`) | `ImmutableArray<T>` (compares by reference) |
| Stable hash across processes | `HashHelper.ComputeFnv1AHash`, `HashHelper.ComputeFnv1A64`, `HashHelper.Combine` | `string.GetHashCode()` |

## 7. Adding a helper

- Add one when the third copy of the same logic appears, or when one definition must stay consistent across analyzers and
  emitters (as `GpuExecutionSpace` and `GpuMacroClassifier` do). Two similar lines in one file are not a reason.
- Where it goes:
  - Roslyn-only queries with no G# meaning: `Extensions/`.
  - G# meaning shared by analyzers and the transpiler: a named static class at the project root (`GpuExecutionSpace`).
  - Transpiler-only: `Transpiler/Helpers/`.
  - Generator plumbing: `Helpers/`.
- Then add a row to this file in the same change.
