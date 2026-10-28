namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The members through which <c>MemoryExtensions.Sort</c> compares the elements of a user struct: its
/// <c>IComparer&lt;T&gt;.Compare</c> (a comparer struct, <c>__gsharp_compare</c>) or its <c>IComparable&lt;T&gt;.CompareTo</c>
/// (the default order of a struct key, <c>__gsharp_compare_to</c>).
/// </summary>
/// <remarks>
/// [WHY] As with <c>Dispose</c> (<see cref="UsingTranspiler"/>), every definition of the struct declares the member and
/// only the methods that sort with it define it, after the method body, under an include guard of its own: the body of a
/// <c>Compare</c> written for the host is never transpiled unless device code sorts with it.
/// </remarks>
internal static class SortComparisonMembers
{
    /// <summary>
    /// The implementation of <c>IComparer&lt;<paramref name="key"/>&gt;.Compare</c> in <paramref name="type"/>, when the
    /// struct implements one comparer interface and declares the method with a body in this compilation.
    /// </summary>
    public static bool TryGetCompare(INamedTypeSymbol type, ITypeSymbol key, Compilation compilation,
        [NotNullWhen(true)] out IMethodSymbol? method) =>
        TryGetImplementation(type, key, isComparer: true, compilation, out method);

    /// <summary>
    /// The implementation of <c>IComparable&lt;<paramref name="type"/>&gt;.CompareTo</c> in <paramref name="type"/>, when
    /// the struct declares it with a body in this compilation.
    /// </summary>
    public static bool TryGetCompareTo(INamedTypeSymbol type, Compilation compilation,
        [NotNullWhen(true)] out IMethodSymbol? method) =>
        TryGetImplementation(type, type, isComparer: false, compilation, out method);

    /// <summary>Records that the method sorts with <paramref name="method"/>, whose definition it then writes.</summary>
    public static void Use(IMethodSymbol method, ref TranspileContext context)
    {
        StructTranspiler.RegisterUsedStructIfApplicable(method.ContainingType, ref context);
        var isRecorded = context.Emitter.SortComparisons
            .AsStackEnumerable()
            .Any(method, static (recorded, m) => recorded.IsEqualTo(m));
        if (!isRecorded)
        {
            context.Emitter.SortComparisons.Add(method);
        }
    }

    /// <summary>Appends the C++ name of the member that implements <paramref name="method"/>.</summary>
    public static void AppendMemberName(IMethodSymbol method, SourceWriter writer) =>
        writer.Append(IsCompare(method) ? GSharpInternalNames.Sort.CompareMember : GSharpInternalNames.Sort.CompareToMember);

    /// <summary>Writes the declarations of the comparison members of <paramref name="type"/> inside its definition.</summary>
    public static void WriteDeclarations(INamedTypeSymbol type, ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        if (type.IsGenericType)
        {
            return;
        }

        if (TryGetUniqueComparerKey(type, out var key) && TryGetCompare(type, key, compilation, out var compare))
        {
            WriteDeclaration(compare, ref context);
        }

        if (TryGetCompareTo(type, compilation, out var compareTo))
        {
            WriteDeclaration(compareTo, ref context);
        }
    }

    /// <summary>
    /// Writes the definition of each comparison member that the method sorts with (<see cref="Emitter.SortComparisons"/>),
    /// each under an include guard of its own. Call it after the method body and before the struct definitions are
    /// written: a comparison body can use more structs.
    /// </summary>
    public static void WriteDefinitions(ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        for (var i = 0; i < context.Emitter.SortComparisons.Count; i++)
        {
            var method = context.Emitter.SortComparisons[i];
            if (TryGetBody(method, compilation, out var declaration, out var block, out var expression))
            {
                WriteDefinition(method, declaration, block, expression, ref context);
            }
        }
    }

    // __device__ int <member>(<T> <a>[, <T> <b>]);
    private static void WriteDeclaration(IMethodSymbol method, ref TranspileContext context)
    {
        context.Emitter.Writer.Append("__device__ int ");
        AppendMemberName(method, context.Emitter.Writer);
        WriteParameters(method, ref context);
        context.Emitter.Writer.AppendLine(";");
    }

    // #ifndef __GSHARP_COMPARE_<type>_<member>__
    // #define __GSHARP_COMPARE_<type>_<member>__
    // __device__ inline int <type>::<member>(<T> <a>[, <T> <b>]) { <body> }
    // #endif
    private static void WriteDefinition(IMethodSymbol method, MethodDeclarationSyntax declaration, BlockSyntax? block,
        ExpressionSyntax? expression, ref TranspileContext context)
    {
        var type = method.ContainingType;
        var writer = context.Emitter.Writer;
        writer.Append("#ifndef __GSHARP_COMPARE_").AppendFullyQualifiedCppName(type).Append('_');
        AppendMemberName(method, writer);
        writer.AppendLine("__");
        writer.Append("#define __GSHARP_COMPARE_").AppendFullyQualifiedCppName(type).Append('_');
        AppendMemberName(method, writer);
        writer.AppendLine("__");
        writer.Append("__device__ inline int ").AppendFullyQualifiedCppName(type).Append("::");
        AppendMemberName(method, writer);

        // [WHY] The struct may be declared in another file than the method, so its body is bound with a model of its own tree.
        var query = context.Query;
        var reporter = context.Reporter;
        var model = query.SemanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
        context.Query = new Query(model, query.BclIntrinsicMap, query.UsedStructs);
        context.Reporter = reporter.WithSemanticModel(model);
        var previousStructType = context.Emitter.CurrentStructType;
        context.Emitter.CurrentStructType = type;
        try
        {
            WriteParameters(method, ref context);
            context.Emitter.Writer.Append(' ');
            LambdaTranspilerHelper.TranspileLambdaBody(ref context, block, expression);
        }
        finally
        {
            context.Emitter.CurrentStructType = previousStructType;
            context.Query = query;
            context.Reporter = reporter;
        }

        context.Emitter.Writer.AppendLine("#endif").AppendLine();
    }

    private static void WriteParameters(IMethodSymbol method, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Parentheses())
        {
            for (var i = 0; i < method.Parameters.Length; i++)
            {
                context.Emitter.Writer.AppendIf(i > 0, ",");
                context.Emitter.Dialect.AppendType(ref context, method.Parameters[i].Type);
                context.Emitter.Writer.Append(' ');
                CppNameResolver.AppendSymbolName(method.Parameters[i], ref context);
            }
        }
    }

    private static bool IsCompare(IMethodSymbol method) => method.Parameters.Length == 2;

    private static bool TryGetImplementation(INamedTypeSymbol type, ITypeSymbol key, bool isComparer,
        Compilation compilation, [NotNullWhen(true)] out IMethodSymbol? method)
    {
        method = null;
        foreach (var candidate in type.AllInterfaces)
        {
            if (!IsComparisonInterface(candidate, isComparer) || !candidate.TypeArguments[0].IsEqualTo(key))
            {
                continue;
            }

            var member = candidate.GetMembers(isComparer ? "Compare" : "CompareTo")
                .AsStackEnumerable()
                .FirstOrDefault();
            if (member != null &&
                type.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation &&
                implementation.ContainingType.IsEqualTo(type) &&
                TryGetBody(implementation, compilation, out _, out _, out _))
            {
                method = implementation;
                return true;
            }
        }

        return false;
    }

    /// <summary>The key of the one <c>IComparer&lt;T&gt;</c> that <paramref name="type"/> implements.</summary>
    public static bool TryGetUniqueComparerKey(INamedTypeSymbol type, [NotNullWhen(true)] out ITypeSymbol? key)
    {
        key = null;
        foreach (var candidate in type.AllInterfaces)
        {
            if (!IsComparisonInterface(candidate, isComparer: true))
            {
                continue;
            }

            if (key != null)
            {
                key = null;
                return false;
            }

            key = candidate.TypeArguments[0];
        }

        return key != null;
    }

    // System.Collections.Generic.IComparer<T> or System.IComparable<T>
    private static bool IsComparisonInterface(INamedTypeSymbol candidate, bool isComparer)
    {
        var definition = candidate.OriginalDefinition;
        if (definition.TypeParameters.Length != 1)
        {
            return false;
        }

        return isComparer
            ? definition.Name == "IComparer" && definition.ContainingNamespace is
            {
                Name: "Generic", ContainingNamespace: { Name: "Collections", ContainingNamespace: { } system }
            } && system.IsSystemNamespace()
            : definition.Name == "IComparable" && definition.ContainingNamespace.IsSystemNamespace();
    }

    // The body of `int Compare(...) { ... }` or `=> expr`, declared in a tree of the compilation
    private static bool TryGetBody(IMethodSymbol method, Compilation compilation,
        [NotNullWhen(true)] out MethodDeclarationSyntax? declaration, out BlockSyntax? block, out ExpressionSyntax? expression)
    {
        block = null;
        expression = null;
        if (!method.TryGetDeclaringSyntax(out declaration) || !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            return false;
        }

        block = declaration.Body;
        expression = declaration.ExpressionBody?.Expression;
        return block != null || expression != null;
    }
}
