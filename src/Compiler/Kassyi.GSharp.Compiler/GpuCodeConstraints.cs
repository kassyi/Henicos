namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The G# restrictions on device code, decided in one place: the analyzers report a violation, and the transpiler uses
/// the same answer only to decide whether it can emit the construct.
/// </summary>
/// <remarks>
/// A check written twice drifts; the analyzer and the transpiler then disagree on edge cases (#461). Each member takes
/// the syntax it judges and the semantic model, so that it works on the user's source and on a lowered copy of it.
/// </remarks>
internal static class GpuCodeConstraints
{
    /// <summary>
    /// Whether a GPU function returns <see langword="void"/>, an unmanaged type, or is a valid device iterator
    /// (GSH0004).
    /// </summary>
    public static bool HasSupportedReturnType(IMethodSymbol method) =>
        method.ReturnType.SpecialType == SpecialType.System_Void ||
        method.ReturnType.IsUnmanagedType ||
        IteratorTypeResolver.IsDeviceIteratorMethod(method);

    /// <summary>The return type of <paramref name="method"/> as the GSH0004 message names it.</summary>
    public static string FormatReturnType(IMethodSymbol method) =>
#pragma warning disable RS0030 // Message text only (not a hot path); keeps the C# spelling of the type.
        method.ReturnType.ToDisplayString();
#pragma warning restore RS0030

    /// <summary>Whether <paramref name="method"/> is a <c>[GpuDeviceFunction]</c> that is not static (GSH0066).</summary>
    public static bool IsInstanceDeviceFunction(IMethodSymbol method) =>
        !method.IsStatic && method.HasAttribute(KnownAttributes.GpuDeviceFunction);

    /// <summary>
    /// Whether device code cannot hold a value of <paramref name="type"/> because it is a function pointer, which points into
    /// host code (GSH0003).
    /// </summary>
    public static bool IsFunctionPointer(ITypeSymbol? type) => type is IFunctionPointerTypeSymbol;

    /// <summary>
    /// The name of <paramref name="type"/> when only the .NET runtime can give it meaning, so device code cannot use it
    /// (GSH0003): <c>dynamic</c> (dynamic dispatch through the runtime binder), <see cref="TypedReference"/> (a
    /// reference tracked by the GC), <c>GCHandle</c> (a handle of the GC) or <see langword="decimal"/> (arithmetic in
    /// software of the BCL, with no C++ type or literal); otherwise <see langword="null"/>.
    /// </summary>
    public static string? GetRuntimeOnlyTypeName(ITypeSymbol? type) => type switch
    {
        { TypeKind: TypeKind.Dynamic } => "dynamic",
        { SpecialType: SpecialType.System_Decimal } => "decimal",
        { SpecialType: SpecialType.System_TypedReference } => nameof(TypedReference),
        INamedTypeSymbol { Name: TypeConstants.GCHandleType } named when named.ContainingNamespace.IsSystemRuntimeInteropServices() =>
            TypeConstants.GCHandleType,
        _ => null
    };

    /// <summary>Whether <paramref name="type"/> is <see cref="string"/>, which device code cannot hold (GSH0002).</summary>
    public static bool IsString(ITypeSymbol? type) => type is { SpecialType: SpecialType.System_String };

    /// <summary>
    /// Whether device code can create an object of <paramref name="type"/> with <c>new</c>: a struct or a
    /// <c>SharedMemory&lt;T&gt;</c> (GSH0001). A type that does not resolve is the C# compiler's error, not this one.
    /// </summary>
    public static bool IsSupportedObjectCreation(ITypeSymbol? type) =>
        type is null or IErrorTypeSymbol ||
        type.TypeKind is TypeKind.Error or TypeKind.Struct ||
        type.IsSharedMemoryType();

    /// <summary>The GSH0003 text for <c>with</c> on a class or record class, which allocates a new object.</summary>
    public const string WithOnReferenceType = "with expression on a reference type (heap allocation)";

    /// <summary>
    /// Why <c>with</c> cannot assign <paramref name="member"/> on the device, or <see langword="null"/> when it can
    /// (GSH0003): a <c>[GpuReadOnly]</c> member is <c>const</c> on the device.
    /// </summary>
    /// <param name="member">The member named on the left of a <c>with</c> initializer assignment.</param>
    public static string? GetWithMemberProblem(ISymbol? member)
    {
        var type = member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };
        if (member == null || type == null)
        {
            return null;
        }

        // [WHY] A [GpuReadOnly] array member stays assignable: the device holds it as a GSharpArray of const elements
        return member.HasAttribute(KnownAttributes.GpuReadOnly) && !type.TryGetArrayLikeElementType(out _)
            ? $"with expression assigning the [GpuReadOnly] member '{member.Name}'"
            : null;
    }

    /// <summary>The GSH0003 text for a switch expression arm with a <c>when</c> clause.</summary>
    public const string SwitchArmWhenClauseNotSupported = "Switch expression arms with 'when' clauses are not supported";

    /// <summary>The GSH0003 text for a switch expression arm whose pattern is not a constant or a discard.</summary>
    public const string SwitchArmPatternNotSupported =
        "Switch expression patterns other than constant and discard are not supported";

    /// <summary>Whether a switch expression arm's pattern is one the transpiler emits: a constant or a discard (GSH0003).</summary>
    public static bool IsSupportedSwitchArmPattern(PatternSyntax pattern) =>
        pattern is ConstantPatternSyntax or DiscardPatternSyntax;

    /// <summary>
    /// The GSH0003 text for a barrier in the scope of a <c>using</c>: the CPU state machine returns at a barrier, which
    /// would dispose the resource before the code after the barrier uses it.
    /// </summary>
    public const string BarrierInUsingScope = "barrier in the scope of a using";

    /// <summary>The GSH0003 text for a stack array whose size is not a constant.</summary>
    public const string DynamicArraySize = "dynamic array size";

    /// <summary>Whether the size of a stack array is a constant, which a C++ array declaration needs (GSH0003).</summary>
    public static bool IsConstantArraySize(ExpressionSyntax size, SemanticModel semanticModel) =>
        semanticModel.GetSafeConstantValue(size).HasValue;

    /// <summary>
    /// The size of the stack array that <paramref name="variable"/> declares without an initializer list
    /// (<c>Span&lt;int&gt; s = stackalloc int[n]</c>, <c>int[] a = new int[n]</c>), when it is not a constant (GSH0003).
    /// </summary>
    /// <param name="variable">A declarator of a local declaration or a <c>fixed</c> statement.</param>
    /// <param name="declaredType">The type of the declaration.</param>
    /// <param name="semanticModel">The semantic model of the declaration.</param>
    public static ExpressionSyntax? FindDynamicArraySize(VariableDeclaratorSyntax variable, ITypeSymbol? declaredType,
        SemanticModel semanticModel)
    {
        if (variable.Initializer == null ||
            !InitializationSyntaxExtractor.TryGetArrayElementType(declaredType, out var elementType, out _) ||
            elementType == null ||
            !InitializationSyntaxExtractor.TryGetArrayInitialization(variable.Initializer.Value, out var count,
                out var size, out var initializer))
        {
            return null;
        }

        if (initializer != null && count.HasValue)
        {
            return null;
        }

        return size != null && !IsConstantArraySize(size, semanticModel) ? size : null;
    }

    /// <summary>
    /// Whether <paramref name="dialect"/> emits <c>Gpu.InlinePtx</c>; the others replace the call with a runtime failure
    /// (GSH0054).
    /// </summary>
    public static bool SupportsInlinePtx(ITranspilerDialect dialect) =>
        dialect.Capabilities.HasFlag(DialectCapabilities.InlineAssembly);

    /// <summary>
    /// Whether <paramref name="symbol"/> is a static field or property that lives in host memory, which device code cannot
    /// read (GSH0065). Constants, enum members, the members that <paramref name="dialect"/> maps to an intrinsic and the
    /// members of <c>Kassyi.GSharp.Core</c> types are not.
    /// </summary>
    public static bool IsHostStaticMember(ISymbol symbol, ITranspilerDialect dialect)
    {
        var isStaticData = symbol switch
        {
            IFieldSymbol field => field is { IsStatic: true, IsConst: false } && field.ContainingType?.TypeKind != TypeKind.Enum,
            IPropertySymbol property => property.IsStatic,
            _ => false
        };

        return isStaticData &&
               !dialect.TryMapIntrinsic(symbol, out _) &&
               symbol.ContainingType?.ContainingNamespace.IsCoreNamespace() != true;
    }

    /// <summary>
    /// Whether <paramref name="reference"/>, a reference to <paramref name="symbol"/>, reads a host static member
    /// (<see cref="IsHostStaticMember"/>); a reference inside <c>nameof</c> reads nothing (GSH0065).
    /// </summary>
    public static bool IsHostStaticMemberReference(SyntaxNode reference, ISymbol symbol, SemanticModel semanticModel,
        ITranspilerDialect dialect) =>
        IsHostStaticMember(symbol, dialect) && !IsInsideNameof(reference, semanticModel);

    private static bool IsInsideNameof(SyntaxNode node, SemanticModel semanticModel)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } invocation &&
                semanticModel.GetSymbol(invocation) == null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the argument of <c>Gpu.InlinePtx</c> is text known at compile time: a constant string or an interpolated
    /// string (GSH0006).
    /// </summary>
    public static bool IsInlinePtxText(ExpressionSyntax argument, SemanticModel semanticModel) =>
        argument is InterpolatedStringExpressionSyntax ||
        semanticModel.GetSafeConstantValue(argument) is { HasValue: true, Value: string };

    /// <summary>The factor of <c>Gpu.Hint.Unroll(factor)</c>, when it is a non-negative <see cref="int"/> constant (GSH0016).</summary>
    public static bool TryGetUnrollFactor(ExpressionSyntax factor, SemanticModel semanticModel, out int value) =>
        TryGetMacroConstantInt(factor, semanticModel, out value) && value >= 0;

    /// <summary>
    /// The value of a macro argument that the transpiler expands at compile time (the start and count of
    /// <c>Gpu.ForUnroll</c>), when it is an <see cref="int"/> constant (GSH0012).
    /// </summary>
    public static bool TryGetMacroConstantInt(ExpressionSyntax argument, SemanticModel semanticModel, out int value)
    {
        if (semanticModel.GetSafeConstantValue(argument) is { HasValue: true, Value: int constant })
        {
            value = constant;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// The loop variable of the body of <c>Gpu.ForUnroll</c> or <c>Gpu.GridStrideLoop</c>: <c>i =&gt; ...</c>, or
    /// <c>(ref ctx, i) =&gt; ...</c> for the overload that takes a context (GSH0011).
    /// </summary>
    public static ParameterSyntax? GetMacroLoopParameter(LambdaExpressionSyntax body, bool hasContext) => body switch
    {
        SimpleLambdaExpressionSyntax simpleLambda => simpleLambda.Parameter,
        ParenthesizedLambdaExpressionSyntax parenLambda when hasContext && parenLambda.ParameterList.Parameters.Count == 2 =>
            parenLambda.ParameterList.Parameters[1],
        ParenthesizedLambdaExpressionSyntax parenLambda when !hasContext && parenLambda.ParameterList.Parameters.Count == 1 =>
            parenLambda.ParameterList.Parameters[0],
        _ => null
    };

    /// <summary>The GSH0003 text for a <c>Gpu.SyncGrid(body)</c> call whose argument is not a lambda.</summary>
    public const string SyncGridRequiresLambda = "Gpu.SyncGrid requires a lambda expression argument";

    /// <summary>
    /// The GSH0003 text for a local declared after a <c>return</c> of a <c>Gpu.SyncGrid</c> body that cannot be declared at
    /// the top of the body and assigned later (<see cref="SyncGridBody.IsUnsplittable"/>).
    /// </summary>
    public const string SyncGridUnsplittableDeclaration =
        "ref, scoped, using, pattern or deconstruction local, or shared memory, array or iterator local, declared after a return in a Gpu.SyncGrid body";

    /// <summary>The GSH0003 text for a <c>goto</c> that leaves its statement of a <c>Gpu.SyncGrid</c> body.</summary>
    public const string SyncGridGotoAcrossGaps = "goto out of a statement of a Gpu.SyncGrid body";

    /// <summary>The GSH0003 text for a <c>return</c> with a value in a <c>Gpu.SyncGrid</c> body (C# rejects it as well).</summary>
    public const string SyncGridReturnWithValue = "return with a value in a Gpu.SyncGrid body";

    /// <summary>
    /// The loop that the <c>Gpu.Hint.Unroll</c> statement <paramref name="hint"/> applies to: the next statement of the same
    /// block or switch section, when it is a <c>for</c>, <c>while</c> or <c>do</c> loop (GSH0017).
    /// </summary>
    public static StatementSyntax? GetUnrolledLoop(ExpressionStatementSyntax hint)
    {
        SyntaxList<StatementSyntax> statements;
        switch (hint.Parent)
        {
            case BlockSyntax block:
                statements = block.Statements;
                break;
            case SwitchSectionSyntax switchSection:
                statements = switchSection.Statements;
                break;
            default:
                return null;
        }

        var index = statements.IndexOf(hint);
        if (index < 0 || index + 1 >= statements.Count)
        {
            return null;
        }

        var nextStmt = statements[index + 1];
        return nextStmt is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax ? nextStmt : null;
    }

    /// <summary>
    /// The body of a <c>Gpu.SyncGrid(body)</c> call, when it is written as the call's one argument and that argument is a
    /// lambda (GSH0003).
    /// </summary>
    public static bool TryGetSyncGridBody(InvocationExpressionSyntax invocation, [NotNullWhen(true)] out LambdaExpressionSyntax? body)
    {
        var args = invocation.ArgumentList.Arguments;
        body = args.Count == 1 ? args[0].Expression as LambdaExpressionSyntax : null;
        return body != null;
    }
}
