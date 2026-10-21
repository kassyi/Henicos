namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Selects how a <see cref="CppName"/> is spelled in emitted C++.</summary>
internal enum CppNameKind : byte
{
    /// <summary>Emitted unchanged: compiler-internal names, external names and names not yet renamed (#402).</summary>
    Verbatim,

    /// <summary>A parameter, local, label or local function: <c>__gsharp_id_{ordinal}_{name}</c>.</summary>
    FunctionScope,

    /// <summary>An instance field or property of a struct that G# emits itself: <c>__gsharp_mb_{name}</c>.</summary>
    Member,

    /// <summary>
    /// A name in the global C++ scope, already complete: <c>__gsharp_ty_</c> / <c>__gsharp_tp_</c> / <c>__gsharp_fn_</c> (see
    /// <see cref="GlobalCppNames"/>).
    /// </summary>
    Global
}

/// <summary>The C++ spelling of a user declaration, produced only by <see cref="CppNameResolver"/>.</summary>
/// <remarks>
/// The parts are written separately so that emission does not allocate; <see cref="ToString"/> exists for the few
/// callers that need the name as a string.
/// </remarks>
internal readonly struct CppName
{
    private CppName(CppNameKind kind, int ordinal, string name)
    {
        Kind = kind;
        Ordinal = ordinal;
        Name = name;
    }

    public CppNameKind Kind { get; }

    /// <summary>The declaration ordinal within the declaring method, or -1 when the declaration was not found there.</summary>
    public int Ordinal { get; }

    /// <summary>The C# name without the verbatim <c>@</c>.</summary>
    public string Name { get; }

    internal static CppName Verbatim(string name) => new(CppNameKind.Verbatim, -1, name);

    internal static CppName FunctionScope(int ordinal, string name) => new(CppNameKind.FunctionScope, ordinal, name);

    internal static CppName Member(string name) => new(CppNameKind.Member, -1, name);

    internal static CppName Global(string cppName) => new(CppNameKind.Global, -1, cppName);

    public override string ToString()
    {
        using var writer = new SourceWriter();
        writer.AppendCppName(this);
        return writer.ToString();
    }
}

/// <summary>
/// Derives the C++ name of every user declaration from its symbol, so that declarations and references always agree and
/// no user name can collide with a C++ keyword, a CUDA built-in, a macro or a name that G# emits itself.
/// </summary>
/// <remarks>
/// GSH0057 rejects <c>__</c> in user identifiers, so names under the <c>__gsharp_</c> families cannot be produced by
/// user code. Function-scope names carry the declaration ordinal so that distinct symbols with the same name stay
/// distinct even when lowering flattens their scopes.
/// </remarks>
internal static class CppNameResolver
{
    private static readonly ConditionalWeakTable<SyntaxNode, int[]> s_declarationPositions = new();

    /// <summary>Resolves the C++ name for <paramref name="symbol"/>, or <paramref name="fallbackName"/> when it is unresolved.</summary>
    public static CppName Resolve(ISymbol? symbol, string fallbackName, ITranspilerDialect dialect) => symbol switch
    {
        ILocalSymbol or IParameterSymbol or ILabelSymbol or IMethodSymbol { MethodKind: MethodKind.LocalFunction } =>
            CppName.FunctionScope(GetDeclarationOrdinal(symbol), symbol.Name),
        IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false } when IsEmittedStructMember(symbol, dialect)
            => CppName.Member(symbol.Name),
        ITypeParameterSymbol parameter => CppName.Global(GlobalCppNames.GetTypeParameterName(parameter)),
        IMethodSymbol method when IsEmittedDeviceFunction(method, dialect) =>
            CppName.Global(GlobalCppNames.GetFunctionName(method)),
        INamedTypeSymbol type when GlobalCppNames.IsEmittedUserType(type) && !dialect.IsMappedType(type) =>
            CppName.Global(GlobalCppNames.GetTypeName(type)),
        _ => CppName.Verbatim(fallbackName)
    };

    /// <summary>Emits the name declared by <paramref name="declaration"/>.</summary>
    public static void AppendDeclaredName(SyntaxNode declaration, SyntaxToken identifier, ref TranspileContext context)
    {
        var symbol = context.Query.SemanticModel.GetDeclaredSymbol<ISymbol>(declaration);
        context.Emitter.Writer.AppendCppName(Resolve(symbol, identifier.ValueText, context.Emitter.Dialect));
    }

    /// <summary>Emits the name that <paramref name="reference"/> binds to.</summary>
    /// <remarks>
    /// Lowering synthesizes references that carry the original span of a declarator rather than of a reference, so the
    /// declared symbol is the fallback.
    /// </remarks>
    public static void AppendReferencedName(SyntaxNode reference, SyntaxToken identifier, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var symbol = model.GetSymbol(reference) ?? model.GetDeclaredSymbol<ISymbol>(reference);
        context.Emitter.Writer.AppendCppName(Resolve(symbol, identifier.ValueText, context.Emitter.Dialect));
    }

    /// <summary>Emits the name of <paramref name="symbol"/>.</summary>
    public static void AppendSymbolName(ISymbol symbol, ref TranspileContext context) =>
        context.Emitter.Writer.AppendCppName(Resolve(symbol, symbol.Name, context.Emitter.Dialect));

    public static ref readonly SourceWriter AppendCppName(this in SourceWriter writer, in CppName name)
    {
        switch (name.Kind)
        {
            case CppNameKind.FunctionScope:
                writer.Append(GSharpInternalNames.RenamedIdentifierPrefix);
                if (name.Ordinal >= 0)
                {
                    writer.Append(name.Ordinal).Append('_');
                }

                break;
            case CppNameKind.Member:
                writer.Append(GSharpInternalNames.RenamedMemberPrefix);
                break;
        }

        writer.Append(name.Name);
        return ref writer;
    }

    /// <summary>
    /// Whether <paramref name="method"/> is a <c>[GpuDeviceFunction]</c> that G# emits as a global C++ function, rather
    /// than an intrinsic that the dialect maps to a built-in.
    /// </summary>
    public static bool IsEmittedDeviceFunction(IMethodSymbol method, ITranspilerDialect dialect) =>
        method is { MethodKind: MethodKind.Ordinary, IsStatic: true } &&
        method.OriginalDefinition.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
        !dialect.TryMapIntrinsic(method, out _);

    private static bool IsEmittedStructMember(ISymbol member, ITranspilerDialect dialect) =>
        member.ContainingType is { TypeKind: TypeKind.Struct } type &&
        !type.ContainingNamespace.IsSystemOrNumerics() &&
        !type.IsSharedMemoryType() &&
        !dialect.IsMappedType(type);

    private static int GetDeclarationOrdinal(ISymbol symbol)
    {
        if (symbol.Locations.Length == 0 || symbol.Locations[0] is not { IsInSource: true, SourceTree: { } tree } location)
        {
            return -1;
        }

        var method = GetDeclaringMethodSyntax(symbol, tree, location.SourceSpan);
        if (method == null)
        {
            return -1;
        }

        var positions = s_declarationPositions.GetValue(method, CollectDeclarationPositions);
        var index = Array.BinarySearch(positions, location.SourceSpan.Start);
        return index >= 0 ? index : -1;
    }

    private static SyntaxNode? GetDeclaringMethodSyntax(ISymbol symbol, SyntaxTree tree, TextSpan span)
    {
        var container = symbol.ContainingSymbol;
        while (container is IMethodSymbol { MethodKind: MethodKind.LambdaMethod or MethodKind.LocalFunction })
        {
            container = container.ContainingSymbol;
        }

        if (container == null)
        {
            return null;
        }

        foreach (var reference in container.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree == tree && reference.Span.Contains(span))
            {
                return reference.GetSyntax();
            }
        }

        return null;
    }

    // [WHY] Ordinals come from identifier positions rather than from symbols, because symbols obtained from different
    // semantic models (signature, body, CPU launcher) are not guaranteed to compare equal.
    private static int[] CollectDeclarationPositions(SyntaxNode method)
    {
        var positions = ImmutableArray.CreateBuilder<int>();
        foreach (var node in method.DescendantNodesAndSelf())
        {
            var identifier = node switch
            {
                ParameterSyntax p => p.Identifier,
                VariableDeclaratorSyntax v => v.Identifier,
                SingleVariableDesignationSyntax d => d.Identifier,
                ForEachStatementSyntax f => f.Identifier,
                LocalFunctionStatementSyntax l => l.Identifier,
                LabeledStatementSyntax l => l.Identifier,
                CatchDeclarationSyntax c => c.Identifier,
                _ => default
            };

            if (!identifier.IsKind(SyntaxKind.None) && !identifier.IsMissing)
            {
                positions.Add(identifier.SpanStart);
            }
        }

        var result = positions.ToArray();
        Array.Sort(result);
        return result;
    }
}
