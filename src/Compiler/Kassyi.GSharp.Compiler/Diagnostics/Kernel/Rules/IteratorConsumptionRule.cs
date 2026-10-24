namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports an <c>IEnumerable&lt;T&gt;</c> iterator value that is passed as an argument, returned, reassigned, declared
/// without an initializer, or selected conditionally instead of being enumerated directly by <c>foreach</c> (GSH0071).
/// </summary>
internal sealed class IteratorConsumptionRule :
    ISyntaxValidationRule<InvocationExpressionSyntax>,
    ISyntaxValidationRule<IdentifierNameSyntax>,
    ISyntaxValidationRule<ConditionalExpressionSyntax>,
    ISyntaxValidationRule<BinaryExpressionSyntax>,
    ISyntaxValidationRule<AssignmentExpressionSyntax>,
    ISyntaxValidationRule<VariableDeclaratorSyntax>
{
    public static readonly IteratorConsumptionRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace);

    private IteratorConsumptionRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is not IMethodSymbol method ||
            !IteratorTypeResolver.IsDeviceIteratorMethod(method))
        {
            return;
        }

        var top = IteratorTypeResolver.UnwrapParentheses(node);
        if (IsDirectForEachOrLocalInitializer(top) || IsHandledByEnclosingRule(top))
        {
            return;
        }

        context.ReportDiagnostic(
            GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace,
            node,
            IteratorTypeResolver.FormatSyntax(node));
    }

    public void Analyze(IdentifierNameSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is not ILocalSymbol local ||
            !IteratorTypeResolver.IsGenericIEnumerable(local.Type, out _))
        {
            return;
        }

        var top = IteratorTypeResolver.UnwrapParentheses(node);
        if (top.Parent is ForEachStatementSyntax forEach && forEach.Expression == top ||
            IsHandledByEnclosingRule(top))
        {
            return;
        }

        context.ReportDiagnostic(
            GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace,
            node,
            node.Identifier.ValueText);
    }

    public void Analyze(ConditionalExpressionSyntax node, ref ValidationContext context)
    {
        if (!IteratorTypeResolver.IsIteratorValueExpression(node.WhenTrue, context.SemanticModel) &&
            !IteratorTypeResolver.IsIteratorValueExpression(node.WhenFalse, context.SemanticModel))
        {
            return;
        }

        if (IteratorTypeResolver.UnwrapParentheses(node).Parent is ConditionalExpressionSyntax)
        {
            return;
        }

        context.ReportDiagnostic(
            GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace,
            node,
            IteratorTypeResolver.FormatSyntax(node));
    }

    public void Analyze(BinaryExpressionSyntax node, ref ValidationContext context)
    {
        if (!node.IsKind(SyntaxKind.CoalesceExpression))
        {
            return;
        }

        if (!IteratorTypeResolver.IsIteratorValueExpression(node.Left, context.SemanticModel) &&
            !IteratorTypeResolver.IsIteratorValueExpression(node.Right, context.SemanticModel))
        {
            return;
        }

        context.ReportDiagnostic(
            GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace,
            node,
            IteratorTypeResolver.FormatSyntax(node));
    }

    public void Analyze(AssignmentExpressionSyntax node, ref ValidationContext context)
    {
        if (!IteratorTypeResolver.IsIteratorValueExpression(node.Left, context.SemanticModel) &&
            !IteratorTypeResolver.IsIteratorValueExpression(node.Right, context.SemanticModel))
        {
            return;
        }

        context.ReportDiagnostic(
            GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace,
            node,
            IteratorTypeResolver.FormatSyntax(node.Left));
    }

    public void Analyze(VariableDeclaratorSyntax node, ref ValidationContext context)
    {
        if (node.Parent is not VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax } ||
            context.SemanticModel.GetDeclaredSymbol(node) is not ILocalSymbol local ||
            !IteratorTypeResolver.IsGenericIEnumerable(local.Type, out _))
        {
            return;
        }

        if (node.Initializer is null)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace,
                node,
                node.Identifier.ValueText);
            return;
        }

        var value = IteratorTypeResolver.UnwrapParentheses(node.Initializer.Value);
        if (IteratorTypeResolver.IsIteratorCandidateInvocation(value, context.SemanticModel) ||
            IsAlreadyReportedInitializer(value, context.SemanticModel))
        {
            return;
        }

        context.ReportDiagnostic(
            GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace,
            node,
            node.Identifier.ValueText);
    }

    private static bool IsAlreadyReportedInitializer(ExpressionSyntax value, SemanticModel semanticModel) =>
        value switch
        {
            ConditionalExpressionSyntax conditional =>
                IteratorTypeResolver.IsIteratorValueExpression(conditional.WhenTrue, semanticModel) ||
                IteratorTypeResolver.IsIteratorValueExpression(conditional.WhenFalse, semanticModel),
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } binary =>
                IteratorTypeResolver.IsIteratorValueExpression(binary.Left, semanticModel) ||
                IteratorTypeResolver.IsIteratorValueExpression(binary.Right, semanticModel),
            AssignmentExpressionSyntax assignment =>
                IteratorTypeResolver.IsIteratorValueExpression(assignment.Left, semanticModel) ||
                IteratorTypeResolver.IsIteratorValueExpression(assignment.Right, semanticModel),
            IdentifierNameSyntax identifier =>
                semanticModel.GetSymbol(identifier) is ILocalSymbol local &&
                IteratorTypeResolver.IsGenericIEnumerable(local.Type, out _),
            _ => false
        };

    private static bool IsDirectForEachOrLocalInitializer(ExpressionSyntax top) =>
        top.Parent is ForEachStatementSyntax forEach && forEach.Expression == top ||
        top.Parent is EqualsValueClauseSyntax
        {
            Parent: VariableDeclaratorSyntax
            {
                Parent: VariableDeclarationSyntax
                {
                    Parent: LocalDeclarationStatementSyntax
                }
            }
        };

    private static bool IsHandledByEnclosingRule(ExpressionSyntax top) =>
        top.Parent is ConditionalExpressionSyntax or
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } or
            AssignmentExpressionSyntax ||
        top.Parent is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "GetEnumerator",
            Parent: InvocationExpressionSyntax
        };
}
