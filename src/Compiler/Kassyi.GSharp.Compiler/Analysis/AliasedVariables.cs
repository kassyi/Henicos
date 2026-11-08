namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The locals and parameters of a member that have another name besides their own: their address is taken
/// (<c>&amp;x</c>), a <c>ref</c> expression names them (<c>ref int r = ref x;</c>, <c>return ref x;</c>), or a lambda
/// or local function captures them (the lambda of a Gpu macro such as <c>Gpu.ForUnroll</c> is expanded in place and
/// captures nothing). A write through the other name changes them, so <see cref="ExpressionEffects"/> treats
/// them as memory.
/// </summary>
/// <remarks>
/// <para>
/// Computed once per member declaration (the outermost one, which holds its lambdas and local functions) and cached for
/// the life of its syntax node.
/// </para>
/// <para>
/// [WHY] The cache holds the spans of the declarations, not their symbols: Roslyn reuses the syntax tree of an
/// unchanged file in the next compilation, where the symbols are new objects, so a cache of symbols would answer false
/// for every later compilation (and keep the old one alive). A span is the same in every compilation that shares the
/// tree.
/// </para>
/// </remarks>
internal static class AliasedVariables
{
    private static readonly ConditionalWeakTable<SyntaxNode, HashSet<TextSpan>> s_aliased = new();

    /// <summary>
    /// Whether <paramref name="variable"/>, a local or parameter declared in source, has another name.
    /// </summary>
    public static bool Contains(ISymbol variable, Compilation compilation)
    {
        if (variable.DeclaringSyntaxReferences.Length == 0 ||
            variable.DeclaringSyntaxReferences[0] is not { } reference ||
            !compilation.ContainsSyntaxTree(reference.SyntaxTree) ||
            GetOutermostMember(reference.GetSyntax()) is not { } member)
        {
            return false;
        }

        return s_aliased.GetValue(member, m => Collect(m, compilation)).Contains(reference.Span);
    }

    // The outermost member that holds node. A top-level statement (GlobalStatementSyntax) counts as its own member;
    // device code never consists of top-level statements, so they are not followed further.
    private static SyntaxNode? GetOutermostMember(SyntaxNode node)
    {
        SyntaxNode? member = null;
        for (var current = node; current != null; current = current.Parent)
        {
            if (current is MemberDeclarationSyntax and not BaseTypeDeclarationSyntax
                and not BaseNamespaceDeclarationSyntax)
            {
                member = current;
            }
        }

        return member;
    }

    private static HashSet<TextSpan> Collect(SyntaxNode member, Compilation compilation)
    {
        var model = compilation.GetCachedSemanticModel(member.SyntaxTree);
        var aliased = new HashSet<TextSpan>();
        foreach (var node in member.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case PrefixUnaryExpressionSyntax addressOf when addressOf.IsKind(SyntaxKind.AddressOfExpression):
                    AddRoot(addressOf.Operand, model, aliased);
                    break;
                case RefExpressionSyntax reference:
                    AddRoot(reference.Expression, model, aliased);
                    break;
                case InvocationExpressionSyntax invocation
                    when model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { RefKind: not RefKind.None }:
                    AddReferenceArguments(invocation, model, aliased);
                    break;
                case AnonymousFunctionExpressionSyntax lambda when !IsMacroBody(lambda, model):
                    AddCaptured(lambda, model, aliased);
                    break;
                case LocalFunctionStatementSyntax localFunction:
                    AddCaptured(localFunction, model, aliased);
                    break;
            }
        }

        return aliased;
    }

    // A call that returns by reference may return one of its ref arguments: Choose(ref x, ref y)
    private static void AddReferenceArguments(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        HashSet<TextSpan> aliased
    )
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword, SyntaxKind.InKeyword))
            {
                AddRoot(argument.Expression, model, aliased);
            }
        }
    }

    // The lambda of a Gpu macro (Gpu.ForUnroll, Gpu.GridStrideLoop ...), whose body the transpiler expands in place: it
    // is not a closure, so what it names is not captured
    private static bool IsMacroBody(AnonymousFunctionExpressionSyntax lambda, SemanticModel model) =>
        lambda.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax invocation } &&
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol macro &&
        GpuMacroClassifier.Classify(macro).HasBody;

    // The locals and parameters declared outside function that its body names
    private static void AddCaptured(SyntaxNode function, SemanticModel model, HashSet<TextSpan> aliased) =>
        function.DescendantNodes()
            .AsStackEnumerable()
            .OfType<IdentifierNameSyntax>()
            .Select((Model: model, Function: function), static (identifier, s) =>
                s.Model.GetSymbolInfo(identifier).Symbol is { } symbol &&
                symbol is ILocalSymbol or IParameterSymbol &&
                !IsDeclaredIn(symbol, s.Function)
                    ? symbol
                    : null
            ).OfType<ISymbol>()
            .Do(aliased, static (symbol, set) => AddDeclaration(symbol, set));

    private static void AddDeclaration(ISymbol variable, HashSet<TextSpan> aliased)
    {
        if (variable.DeclaringSyntaxReferences.Length > 0)
        {
            aliased.Add(variable.DeclaringSyntaxReferences[0].Span);
        }
    }

    private static bool IsDeclaredIn(ISymbol symbol, SyntaxNode function) =>
        symbol.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(function, static (reference, f) =>
                reference.SyntaxTree == f.SyntaxTree && f.Span.Contains(reference.Span)
            );

    // The local or parameter that a location starts at: x, x.Field, x.Inline[i] (a struct holds its fields)
    private static void AddRoot(ExpressionSyntax expression, SemanticModel model, HashSet<TextSpan> aliased)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression) &&
                                                              model.GetTypeInfo(access.Expression).Type is
                                                                  { IsValueType: true }:
                    expression = access.Expression;
                    continue;
                case ElementAccessExpressionSyntax element when model.GetTypeInfo(element.Expression).Type is
                    { IsValueType: true }:
                    expression = element.Expression;
                    continue;
                case IdentifierNameSyntax identifier:
                    if (model.GetSymbolInfo(identifier).Symbol is { } variable &&
                        variable is ILocalSymbol or IParameterSymbol { RefKind: RefKind.None })
                    {
                        AddDeclaration(variable, aliased);
                    }

                    return;
                default:
                    return;
            }
        }
    }
}
