namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles pattern matching expressions (such as <c>x is null</c> or <c>x is not null</c>) for array-like and nullable array-like types.
/// </summary>
public sealed class IsPatternExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<IsPatternExpressionSyntax>
{
    public override void Transpile(IsPatternExpressionSyntax node, ref TranspileContext context)
    {
        if (TryTranspileTypeOrEmptyPattern(node, ref context))
        {
            return;
        }

        if (NullRepresentation.IsNullComparable(context.Query.SemanticModel.GetTypeSymbol(node.Expression)))
        {
            if (node.Pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax literal } &&
                literal.IsKind(SyntaxKind.NullLiteralExpression))
            {
                NullRepresentation.AppendNullTest(node.Expression, isNull: true, ref context);
                return;
            }

            if (node.Pattern is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern } notPattern &&
                notPattern.Pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax notLiteral } &&
                notLiteral.IsKind(SyntaxKind.NullLiteralExpression))
            {
                NullRepresentation.AppendNullTest(node.Expression, isNull: false, ref context);
                return;
            }
        }

        context.Visitor.DefaultVisit(node);
    }

    // x is not T and x is {} / x is not {}: the types decide them as they decide x is T (TypeTests); {} is not null
    private static bool TryTranspileTypeOrEmptyPattern(IsPatternExpressionSyntax node, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var operandType = model.GetTypeSymbol(node.Expression);
        var negated = node.Pattern is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern };
        var pattern = node.Pattern is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern } not
            ? not.Pattern
            : node.Pattern;
        if (pattern is RecursivePatternSyntax
            {
                Type: null,
                PositionalPatternClause: null,
                Designation: null,
                PropertyPatternClause.Subpatterns.Count: 0
            } &&
            NullRepresentation.IsNullComparable(operandType))
        {
            NullRepresentation.AppendNullTest(node.Expression, isNull: negated, ref context);
            return true;
        }

        if (pattern is not TypePatternSyntax typePattern)
        {
            return false;
        }

        var plan = TypeTests.Plan(false, operandType, model.GetTypeSymbol(typePattern.Type), model.Compilation);
        if (!negated)
        {
            return TypeTests.TryAppend(in plan, node.Expression, ref context);
        }

        switch (plan.Kind)
        {
            case TypeTestKind.Constant:
                var inverted = new TypeTestPlan(TypeTestKind.Constant, !plan.Value);
                return TypeTests.TryAppend(in inverted, node.Expression, ref context);
            case TypeTestKind.NotNull:
                NullRepresentation.AppendNullTest(node.Expression, isNull: true, ref context);
                return true;
            default:
                return false;
        }
    }
}
