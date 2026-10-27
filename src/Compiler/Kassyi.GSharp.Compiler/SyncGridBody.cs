namespace Kassyi.GSharp.Compiler;

/// <summary>A statement of a flattened <c>Gpu.SyncGrid(body)</c> and the body it was written in.</summary>
/// <param name="Node">
/// The statement, or the expression of an expression-bodied lambda (<c>Gpu.SyncGrid(() =&gt; x += 1)</c>).
/// </param>
/// <param name="BodyIndex">The index of the body in <see cref="SyncGridStatements.BodyParents"/>; 0 is the outermost.</param>
internal readonly record struct SyncGridElement(CSharpSyntaxNode Node, int BodyIndex);

/// <summary>The statements of a <c>Gpu.SyncGrid(body)</c>, the bodies of nested <c>Gpu.SyncGrid(body)</c> statements flattened in.</summary>
/// <param name="Elements">The statements in source order.</param>
/// <param name="BodyParents">
/// For each body, the index of the body it is nested in; -1 for the outermost body (index 0).
/// </param>
internal readonly record struct SyncGridStatements(
    ImmutableArray<SyncGridElement> Elements,
    ImmutableArray<int> BodyParents)
{
    /// <summary>Whether the statements hold anything other than empty statements.</summary>
    public bool HasExecutableStatements => Elements
        .AsStackEnumerable()
        .Any(static element => element.Node is not EmptyStatementSyntax);
}

/// <summary>
/// The statements of a <c>Gpu.SyncGrid(body)</c> and the gaps between them that get a grid sync. One definition for the
/// CUDA emission (<c>SyncGridMacroRule</c>) and the CPU lowering (<see cref="PreLoweringExpander"/>), so that both
/// dialects synchronize the grid at the same places (#626).
/// </summary>
/// <remarks>
/// <para>
/// A grid sync goes between two statements of the body. Nested <c>Gpu.SyncGrid(body)</c> statements are flattened into
/// the list, so their statements take part in the same gaps. There is no sync after the last statement, next to an
/// explicit <c>Gpu.SyncGrid()</c>, or around an empty statement or a local function declaration (a declaration does not
/// run).
/// </para>
/// <para>
/// With a semantic model, <c>Gpu.SyncGrid</c> is recognized by its symbol (<see cref="GpuMacroClassifier"/>). Without one
/// (the Tier 2 lowering tests), by its syntax: <c>Gpu.SyncGrid(...)</c> or <c>SyncGrid(...)</c>, which also matches a
/// user type named <c>Gpu</c>.
/// </para>
/// </remarks>
internal static class SyncGridBody
{
    /// <summary>Gets the lambda of a <c>Gpu.SyncGrid(body)</c> expression statement.</summary>
    /// <returns><see langword="false"/> for any other statement, and for a <c>Gpu.SyncGrid</c> whose argument is not a lambda.</returns>
    public static bool TryGetBody(
        StatementSyntax statement,
        SemanticModel? model,
        [NotNullWhen(true)] out LambdaExpressionSyntax? body)
    {
        body = null;
        return statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } &&
               IsSyncGrid(invocation, model, withBody: true) &&
               GpuCodeConstraints.TryGetSyncGridBody(invocation, out body);
    }

    /// <summary>Flattens the statements of <paramref name="body"/>; an expression body is one statement, its expression.</summary>
    public static SyncGridStatements Flatten(LambdaExpressionSyntax body, SemanticModel? model)
    {
        var elements = ImmutableArray.CreateBuilder<SyncGridElement>();
        var bodyParents = ImmutableArray.CreateBuilder<int>();
        FlattenBody(body.Body, parent: -1, model, elements, bodyParents);
        return new SyncGridStatements(elements.ToImmutable(), bodyParents.ToImmutable());
    }

    /// <summary>
    /// Whether a grid sync follows <c>elements[index]</c>: not after the last executable statement, not next to an
    /// explicit <c>Gpu.SyncGrid()</c>, and not around an empty statement or a local function declaration.
    /// </summary>
    public static bool IsSyncPoint(ImmutableArray<SyncGridElement> elements, int index, SemanticModel? model)
    {
        var current = elements[index].Node;
        if (IsSkipped(current) || IsExplicitSync(current, model))
        {
            return false;
        }

        for (var next = index + 1; next < elements.Length; next++)
        {
            if (IsSkipped(elements[next].Node))
            {
                continue;
            }

            return !IsExplicitSync(elements[next].Node, model);
        }

        return false;
    }

    /// <summary>Whether <paramref name="statement"/> is a parameterless <c>Gpu.SyncGrid()</c> (statement or expression).</summary>
    public static bool IsExplicitSync(CSharpSyntaxNode statement, SemanticModel? model)
    {
        var invocation = statement switch
        {
            ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv } => inv,
            InvocationExpressionSyntax inv => inv,
            _ => null
        };
        return invocation != null && IsSyncGrid(invocation, model, withBody: false);
    }

    // Statements that neither run nor separate two running statements
    private static bool IsSkipped(CSharpSyntaxNode statement) =>
        statement is EmptyStatementSyntax or LocalFunctionStatementSyntax;

    private static void FlattenBody(
        CSharpSyntaxNode body,
        int parent,
        SemanticModel? model,
        ImmutableArray<SyncGridElement>.Builder elements,
        ImmutableArray<int>.Builder bodyParents)
    {
        var bodyIndex = bodyParents.Count;
        bodyParents.Add(parent);
        switch (body)
        {
            case BlockSyntax block:
                foreach (var statement in block.Statements)
                {
                    if (TryGetBody(statement, model, out var nested))
                    {
                        FlattenBody(nested.Body, bodyIndex, model, elements, bodyParents);
                    }
                    else
                    {
                        elements.Add(new SyncGridElement(statement, bodyIndex));
                    }
                }

                break;
            case ExpressionSyntax expression:
                elements.Add(new SyncGridElement(expression, bodyIndex));
                break;
        }
    }

    private static bool IsSyncGrid(InvocationExpressionSyntax invocation, SemanticModel? model, bool withBody)
    {
        if (model != null)
        {
            return model.GetSymbol(invocation) is IMethodSymbol symbol &&
                   GpuMacroClassifier.Classify(symbol) is { Kind: GpuMacroKind.SyncGrid } call &&
                   call.HasBody == withBody;
        }

        var isSyncGridSyntax = invocation.Expression switch
        {
            MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: TypeConstants.GpuType },
                Name.Identifier.ValueText: TypeConstants.GpuMacros.SyncGrid
            } => true,
            IdentifierNameSyntax { Identifier.ValueText: TypeConstants.GpuMacros.SyncGrid } => true,
            _ => false
        };
        return isSyncGridSyntax && invocation.ArgumentList.Arguments.Count == (withBody ? 1 : 0);
    }
}
