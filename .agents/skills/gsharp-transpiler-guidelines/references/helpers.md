# Compiler Helper Catalog

Read this before adding code to `src/Compiler/Kassyi.GSharp.Compiler`. Each row maps an intent to the helper that already
implements it. If a row fits, use the helper; do not write the logic again. Paths are relative to the compiler project.

A method called on a receiver (an extension or instance method) is written with a leading dot: `.GetSymbol` means
`model.GetSymbol(node)`, `.AppendIf` means `writer.AppendIf(...)`. Static members are written `Type.Member`.
`scripts/check-helpers.js` checks both forms and reports a receiver method written as `Type.Member`; run it after adding
or renaming a helper.
Script conventions and the catalog of agent scripts are in [agent-scripts.md](../../../rules/agent-scripts.md).

## 1. Symbols and attributes

| Intent | Use | Do not write |
| ----- | ----- | ----- |
| Is this a kernel or a device function? | `GpuExecutionSpace.IsGpuFunction` | `HasAttribute(GpuKernel) \|\| HasAttribute(GpuDeviceFunction)` |
| Is this position inside device code (lambdas and local functions included)? | `GpuExecutionSpace.IsWithinGpuFunction` on `GetEnclosingSymbol(...)` | a hand-written `ContainingSymbol` loop |
| Does a symbol carry a G# attribute? | `.HasAttribute` with a `KnownAttributes` constant (also checks the declaring namespace, `KnownAttributes.IsDeclaredInOwnerNamespace`) | `AttributeClass?.Name == "GpuKernelAttribute"` |
| Get the attribute data | `.GetAttribute` | `GetAttributes()` plus a loop |
| Is this a `[Constant]` (macro-injected) parameter? | `.IsConstantParameter` | `p.HasAttribute(KnownAttributes.Constant)` |
| Is this a cooperative kernel (`[GpuKernel(Cooperative = true)]`, launched with `cuLaunchCooperativeKernel`)? | `.IsCooperativeKernel` | `GetAttribute(GpuKernel)` plus a search of `NamedArguments` for `"Cooperative"` |
| Compare symbols | `.IsEqualTo` | `SymbolEqualityComparer.Default.Equals` spelled out |
| Query a collection (`Any`, `All`, `Count`, `First`, `Where` / `Select`) | `.AsStackEnumerable` on an array, `ImmutableArray`, `EquatableArray`, `IReadOnlyList`, `HashSet`, `Dictionary`, `SyntaxList`, `SeparatedSyntaxList`, `SyntaxTokenList` or a node walk (`DescendantNodes()`), then the StackLinq operator with a `static` lambda (`Kassyi.ZeroAlloc.Linq.Embedded`); an outer value goes through the state overload (`Any(shortName, static (a, name) => ...)`) | a hand-written search loop, `System.Linq`, a capturing lambda |
| Collect, act on each element, flatten, or search with a "found" result | the host-only StackLinq operators `AddTo` / `AddIf` (into a collection the caller owns), `Do` / `DoIf`, `SelectMany`, `TryFirst`, `IndexOf`, `OfType<T>()` (filter by type; drops the `null`s a `Select` returned for misses); remove duplicates with `AddIf(list, new HashSet<T>(), static (x, seen) => seen.Add(x))` | a `foreach` that adds to a list or sets a `found` flag and breaks |
| Is the namespace `Kassyi.GSharp.Core` / `System` / `System.Numerics` / `System.Runtime.*`? | `.IsCoreNamespace`, `.IsSystemNamespace`, `.IsSystemOrNumerics`, `.IsSystemRuntimeCompilerServices`, `.IsSystemRuntimeInteropServices` | `ToDisplayString() == "System"` |
| Element type of `T[]`, `Span<T>`, `Memory<T>`, `ImmutableArray<T>` ... | `.TryGetArrayLikeElementType` | a switch over type names |
| Is it `SharedMemory<T>` / `System.Numerics.Vector*`? | `.IsSharedMemoryType`, `.IsSystemNumericsVector` | `Name == "SharedMemory"` |
| Is this a user struct that G# defines on the device? | `GlobalCppNames.IsEmittedUserType` (definition), `GlobalCppNames.IsEncodedUserType` (references, gives way to the dialect type map) | `TypeKind == Struct && !IsSystemOrNumerics() && !IsSharedMemoryType()` spelled out |
| Does a struct need a native shadow struct on the host? | `CudaLayoutRules.IsCustomStruct` (a different classification: also leaves out array-like and Core types) | reusing the device predicate |
| Members that make up a struct's native layout (used struct discovery, device definition, shadow struct, marshaling, `CudaLayoutRules`) | `StructAbiMembers.Get` (a `foreach` over `StructAbiMember`: `Symbol`, `Name`, `Type`, `ElementCount`, `Kind`; allocates nothing) | `GetMembers()` plus `is IFieldSymbol { IsStatic: false }` |
| Is this a computed property (no backing field, `get` only), which the device emits as a getter member function? | `StructAbiMembers.IsComputedProperty` | a search of `GetMembers()` for a field whose `AssociatedSymbol` is the property |
| Native offsets of a struct's fields (`[StructLayout(Pack = n)]` applied) | `CudaLayoutRules.GetFieldLayouts`; the size and alignment of a whole type: `CudaLayoutRules.GetCudaLayout` | an `AlignUp` loop over the fields |
| `[StructLayout(Pack = n)]` | `.TryGetStructLayoutPack` | reading `NamedArguments` by hand |
| First declaring syntax of a symbol | `.TryGetDeclaringSyntax` | `DeclaringSyntaxReferences[0].GetSyntax()` |
| Which `Gpu` macro is this call (`SyncGrid`, `SingleThread`, `ForUnroll` ...)? | `GpuMacroClassifier.Classify` | `symbol.Name == "SyncGrid"` |

Type and namespace names: `TypeConstants` (`TypeConstants.GpuType`, `TypeConstants.GpuMacros`). Attribute short names:
`KnownAttributes`. Never spell `"Gpu"`, `"SharedMemory"` or an attribute name as a literal.

A name alone also matches a user type of the same name. The repository-internal analyzer HNT0003
(`TypeNameCheckAnalyzer` in `tests/Shared/Kassyi.GSharp.Tests.Generators`, run on the compiler build) warns when a
`TypeConstants` type name or a `KnownAttributes` name is compared with a symbol's `Name` and the same member never
looks at a namespace (`ContainingNamespace`, `IsCoreNamespace`, `IsSystem*`, `HasAttribute` ...). When a caller already
checked the namespace, suppress it with `#pragma warning disable HNT0003 // <the caller that checks>`.

## 2. Semantic model and syntax

| Intent | Use | Do not write |
| ----- | ----- | ----- |
| Symbol of a node | `.GetSymbol` | `GetSymbolInfo(node).Symbol` |
| Type of an expression | `.GetTypeSymbol` | `GetTypeInfo(node).Type` |
| Declared symbol of a given kind | `.GetDeclaredSymbol` (`GetDeclaredSymbol<IMethodSymbol>(decl)`) | `GetDeclaredSymbol(decl) as IMethodSymbol` |
| Constant value / operation that may not resolve | `.GetSafeConstantValue`, `.GetSafeOperation` | try/catch around Roslyn calls |
| Location in the user's source after lowering rewrote the tree | `.GetOriginalLocation` | `node.GetLocation()` on a rewritten node |
| Location to report a diagnostic at, for a node that may be in a lowered tree (falls back to the nearest mapped ancestor, then `Location.None`) | `.GetReportLocation` (`context.Reporter.Report(descriptor, node)` uses it) | `node.GetLocation()`: a generator may not report a location outside the compilation |
| Attach or read a lowering annotation (original span, suspending call, state machine `return`) | `LoweringAnnotations.OriginalSpanAnnotationKind`, `LoweringAnnotations.SuspendingCallAnnotationKind`, `LoweringAnnotations.StateMachineReturnAnnotationKind` | `node.HasAnnotations("StateMachineReturn")` |
| Method body, block or `=>` | `.GetBodyNode`, `.HasBody` | `Body ?? (SyntaxNode?)ExpressionBody` |
| Node or token is one of 2-3 kinds | `.IsKind` (`node.IsKind(a, b)`) | `IsKind(a) \|\| IsKind(b)` |
| Walk a tree on a hot path | derive from `PooledWalker` and rent it (see `GpuKernelValidationWalker`) | `new MyWalker()` per call |

## 3. Emitting code

Brackets (`{}`, `()`, `[]`) follow `.agents/rules/transpiler-emission.md` §2, which says when a scope helper is needed;
fixed text (separators, keywords, `;`) is a literal. The other helpers:

| Intent | Use | Do not write |
| ----- | ----- | ----- |
| Text with values in it | `$""` passed straight to `.Append` / `.AppendLine` / `.Line` (zero allocation via `SourceWriterInterpolatedStringHandler`) | a chain of `Append` calls for one line, a separate `StringBuilder`, or `var s = $"..."` then `Append(s)` |
| Conditional text | `.AppendIf`, `.AppendLineIf` | `if (x) writer.Append(...)` |
| C# type name in generated host code | `.AppendCsharpTypeName`, `.AppendFullyQualifiedCsharpName` | `ToDisplayString()` |
| C# namespace name in generated host code | `.AppendNamespace`, `.GetFullNamespaceName` | `ToDisplayString()` |
| How a kernel parameter crosses to the device (array-like, user struct, `ref` / `out`, container, length / span expression) | `KernelParameterShape.Classify` | `GetMarshaledType` plus `IsArrayLikeType` / `IsCustomStruct` / `RefKind` checks, `Name == "ArraySegment"` |
| User identifier in generated host C# | `.ToCSharpIdentifier` | prefixing `@` by hand |
| C++ name of a user symbol (declaration / reference) | `CppNameResolver.AppendDeclaredName`, `CppNameResolver.AppendReferencedName`, `CppNameResolver.AppendSymbolName` | `identifier.ValueText` written directly |
| Compiler-internal name (`__gsharp_*`) | `GSharpInternalNames` (add new names to `Resources/InternalNames.yml`) | a `"__gsharp_..."` literal |
| C++ literal of a constant field | `.AppendConstantField` | `ConstantValue.ToString()` plus a hand-picked suffix |
| Header and attributes of a generated `.g.cs` | `.AppendGeneratedFileHeader`, `.AppendGeneratedCodeAttributes` | copied header text |

Where an internal name lives: in the YAML when the Prelude, the runtime, another emitter, or both host and device code
spell it; otherwise a `private const` in the one emitter that declares and uses it. See the remarks on `GSharpInternalNames`.
The C# names of kernels, device functions and generated launchers follow
[gsharp-naming-conventions.md](../../../rules/gsharp-naming-conventions.md).

## 4. Transpiler rules

A rule is stateless and gets everything through `ref TranspileContext context`:

- `context.Emitter.Writer`: the output (`SourceWriter`).
- `context.Query.SemanticModel`: symbol lookups.
- `context.Reporter.Fail(descriptor, node, args)`: records why the output is incomplete; an analyzer reports the diagnostic.
- `context.Visitor.Visit(child)`: transpile a child node.

| Intent | Use |
| ----- | ----- |
| Handle a new `SyntaxKind` | derive from `SyntaxNodeTranspilerRuleBase` and register it in `TranspilerRuleRegistry` |
| Handle a call to a specific method | implement `IInvocationTranspilerRule` and add it to `TranspilerRuleRegistry.InvocationRules` (order matters) |
| Handle a `Gpu.*` statement macro | implement `IMacroTranspilerRule` and add it to `TranspilerRuleRegistry.MacroRules` |
| Map a BCL method to a CUDA / C++ function | the dialect YAML under `Dialects/`, not a new rule |
| Branch on the target (CUDA or not, threads run as state machines) | `ITranspilerDialect.IsCuda`, `.EmulatesThreadsWithStateMachine` (from the YAML `Backend` and capabilities), not `dialect is ...` or a name comparison |
| Pass a `ref` / `out` argument | `RefSyntaxHelper.TranspileAsByRefArgument` |
| Read a computed property of a struct that G# emits (`s.Sum` → `s.__gsharp_mb_Sum()`) | write the member name as usual, then `ComputedPropertyGetterCall.AppendCallIfGetter` |
| Transpile a lambda body inline | `LambdaTranspilerHelper.TranspileLambdaBody` |
| Enter or suppress automatic grid sync (`Gpu.SyncGrid` bodies, single-thread regions) | `AutoSyncGrid.Enter`, `AutoSyncGrid.Suppress`, `AutoSyncGrid.Restore` in `try` / `finally`; `AutoSyncGrid.IsActive` to read it. Never write `Emitter.AutoSyncGridDepth` directly |
| Null test of an array-like value, `??`, `??=` | `NullRepresentation.IsArrayNullComparable`, `NullRepresentation.AppendArrayNullTest`, `NullRepresentation.AppendCoalesce`, `NullRepresentation.AppendCoalesceAssignment` |

The smallest example is `Transpiler/Rules/ExpressionRules/ParenthesizedExpressionTranspilerRule.cs`.

## 5. Diagnostics

| Intent | Use | Do not write |
| ----- | ----- | ----- |
| Define a diagnostic | an entry in `Diagnostics/diagnostics.json` (generates `GSharpDiagnostics`) | `new DiagnosticDescriptor(...)` |
| Report from an analyzer | `.ReportDiagnostic` (`context.ReportDiagnostic(descriptor, node, args)`) | `Diagnostic.Create(...)` plus `context.ReportDiagnostic` |
| The transpiler cannot emit a construct | `context.Reporter.Fail`, with the check an analyzer reports from (`GpuCodeConstraints`) | throwing, emitting nothing, or reporting the diagnostic from the transpiler as well |
| Check a syntax rule inside kernels | implement `ISyntaxValidationRule` and register it in `DiagnosticRuleRegistry` | a new analyzer that walks kernel bodies again |
| Decide a G# restriction that the analyzer reports and the transpiler must also respect (return type, instance device function, function pointer, `string`, `new`, switch expression arm, stack array size, operator macro target, `Gpu.InlinePtx` text, unroll factor and loop, `Gpu.ForUnroll` constants, loop macro variable, `Gpu.SyncGrid` body, `Gpu.InlinePtx` support, host static member) | `GpuCodeConstraints.HasSupportedReturnType`, `GpuCodeConstraints.IsInstanceDeviceFunction`, `GpuCodeConstraints.IsFunctionPointer`, `GpuCodeConstraints.IsString`, `GpuCodeConstraints.IsSupportedObjectCreation`, `GpuCodeConstraints.IsSupportedSwitchArmPattern`, `GpuCodeConstraints.FindDynamicArraySize`, `GpuCodeConstraints.IsConstantArraySize`, `GpuOperatorMacroTranspiler.TryResolve`, `GpuCodeConstraints.SupportsInlinePtx`, `GpuCodeConstraints.IsHostStaticMember`, `GpuCodeConstraints.IsInlinePtxText`, `GpuCodeConstraints.TryGetUnrollFactor`, `GpuCodeConstraints.TryGetMacroConstantInt`, `GpuCodeConstraints.GetMacroLoopParameter`, `GpuCodeConstraints.GetUnrolledLoop`, `GpuCodeConstraints.TryGetSyncGridBody` | the same condition written again in the rule and in the transpiler |
| The project's dialect (`GSharpTargetDialect`) in an analyzer or the generator; a rule reads it from `ValidationContext.Dialect` | `TranspilerDialects.GetName`, `TranspilerDialects.Create` | `build_property.GSharpTargetDialect` read by hand, `"Cuda"` compared by hand |
| Diagnostic stored in an incremental pipeline model | `.AsEquatable` | a raw `Diagnostic` field |

## 6. Incremental generator models

| Intent | Use | Do not write |
| ----- | ----- | ----- |
| Array field in a pipeline model | `EquatableArray` (`.AsEquatableArray`) | `ImmutableArray<T>` (compares by reference) |
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
