namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Finds the context variable that an operator macro's target takes (#477), and a reference to it in a lowered body.
/// </summary>
internal static class OperatorMacroContext
{
    /// <summary>
    /// Finds the variable that a call of <paramref name="targetMethod"/> passes as its context: the one parameter or local
    /// of the type of the first parameter that is in scope at <paramref name="assignment"/>. Without a single one,
    /// <paramref name="failure"/> is the message argument of GSH0003.
    /// </summary>
    /// <remarks>
    /// [WHY] The operator has no place for the context, so it is found by type, as Scala resolves a <c>using</c> parameter
    /// and Kotlin a context parameter: no candidate or several candidates is an error rather than a guess. C# itself
    /// passes no implicit argument; its nearest construct, <c>[InterpolatedStringHandlerArgument]</c>, names the
    /// argument in an attribute, which would add public API.
    /// </remarks>
    internal static bool TryFindContextVariable(
        AssignmentExpressionSyntax assignment,
        IMethodSymbol targetMethod,
        SemanticModel semanticModel,
        [NotNullWhen(true)] out ISymbol? variable,
        [NotNullWhen(false)] out string? failure)
    {
        variable = null;
        failure = null;
        var contextType = targetMethod.Parameters[0].Type;
        var candidates = ImmutableArray.CreateBuilder<ISymbol>();

        // A lowered node is looked up at its place in the user's source, where the semantic model has its scope
        var location = semanticModel.GetReportLocation(assignment);
        if (location.SourceTree == semanticModel.SyntaxTree)
        {
            var position = location.SourceSpan.Start;
            semanticModel.LookupSymbols(position)
                .AsStackEnumerable()
                .Where((Type: contextType, Position: position), static (symbol, s) =>
                    IsContextCandidate(symbol, s.Type, s.Position)
                ).AddTo(candidates);
        }

        if (candidates.Count == 1)
        {
            variable = candidates[0];
            return true;
        }

        var target = $"{targetMethod.ContainingType.Name}.{targetMethod.Name}";
        var type = contextType.Name;
        failure = candidates.Count == 0
            ? $"Operator macro target '{target}' takes a context of type '{type}', but no parameter or local of that type is in scope"
            : $"Operator macro target '{target}' takes a context of type '{type}', but {candidates.Count} parameters or locals of that type are in scope ({FormatNames(candidates)})";
        return false;
    }

    // A parameter, or a local declared before the call; the scope of a local covers its whole block
    private static bool IsContextCandidate(ISymbol symbol, ITypeSymbol contextType, int position) => symbol switch
    {
        IParameterSymbol parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, contextType),
        ILocalSymbol local => SymbolEqualityComparer.Default.Equals(local.Type, contextType) &&
                              local.Locations.Length > 0 &&
                              local.Locations[0].SourceSpan.End <= position,
        _ => false
    };

    private static string FormatNames(ImmutableArray<ISymbol>.Builder candidates)
    {
        var names = new StringBuilder();
        foreach (var candidate in candidates)
        {
            names.Append(names.Length > 0 ? ", '" : "'").Append(candidate.Name).Append('\'');
        }

        return names.ToString();
    }

    // A reference to variable in the lowered body that holds assignment (__gsharp_sm_ctx-><name> when hoisted), or null
    // when the body is the user's own tree or does not refer to it
    internal static ExpressionSyntax? FindLoweredReference(AssignmentExpressionSyntax assignment, ISymbol variable,
        SemanticModel semanticModel)
    {
        if (assignment.SyntaxTree == semanticModel.SyntaxTree)
        {
            return null;
        }

        var reference = assignment.SyntaxTree.GetRoot()
            .DescendantNodes()
            .AsStackEnumerable()
            .OfType<IdentifierNameSyntax>()
            .FirstOrDefault((Model: semanticModel, Variable: variable), static (identifier, s) =>
                SymbolEqualityComparer.Default.Equals(
                    s.Model.GetSymbol(identifier) ?? s.Model.GetDeclaredSymbol<ISymbol>(identifier),
                    s.Variable
                )
            );

        if (reference != null)
        {
            return reference.Parent is MemberAccessExpressionSyntax access && access.Name == reference
                ? access
                : reference;
        }

        // A non-ref-struct by-value parameter of a split state machine lives in __gsharp_ctx even when the body only
        // passes it implicitly to operator macros and never spells its name (#944).
        if (variable is IParameterSymbol { RefKind: RefKind.None, Type.IsRefLikeType: false } parameter &&
            parameter.TryGetDeclaringSyntax<ParameterSyntax>(out var paramSyntax) &&
            HasSplitStateCases(assignment.SyntaxTree.GetRoot()))
        {
            var treeOrdinal = SemanticModelExtensions.GetSyntaxTreeOrdinal(
                semanticModel.Compilation,
                paramSyntax.SyntaxTree);
            var spanAnnotation = new SyntaxAnnotation(
                LoweringAnnotations.OriginalSpanAnnotationKind,
                LoweringAnnotations.FormatOriginalSpanData(paramSyntax, treeOrdinal));
            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.PointerMemberAccessExpression,
                SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
                SyntaxFactory.IdentifierName(paramSyntax.Identifier.WithoutTrivia())
                    .WithAdditionalAnnotations(spanAnnotation)
            ).WithAdditionalAnnotations(spanAnnotation);
        }

        return null;
    }

    private static bool HasSplitStateCases(SyntaxNode root) =>
        root is BlockSyntax { Statements: [SwitchStatementSyntax stateSwitch] } &&
        stateSwitch.Sections
            .AsStackEnumerable()
            .Any(static section => section.Labels
                .AsStackEnumerable()
                .Any(static label => label is CaseSwitchLabelSyntax
                {
                    Value: LiteralExpressionSyntax { Token.ValueText: "1" }
                }));
}
