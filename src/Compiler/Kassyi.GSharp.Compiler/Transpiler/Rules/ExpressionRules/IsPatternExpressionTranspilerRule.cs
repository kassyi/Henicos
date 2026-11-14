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

        // A type test that no rule writes needs the run-time type of a heap object
        if (HasTypeTest(node.Pattern))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, TypeTests.RuntimeTypeNeeded);
            context.Visitor.AppendUnsupportedPlaceholder(node);
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

        if (pattern is DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax designation } declaration)
        {
            return TryTranspileDeclaration(node, declaration, designation, ref context);
        }

        var testedType = pattern switch
        {
            TypePatternSyntax typePattern => typePattern.Type,
            DeclarationPatternSyntax { Designation: DiscardDesignationSyntax } discard => discard.Type,
            _ => null
        };
        if (testedType is null)
        {
            return false;
        }

        var plan = TypeTests.Plan(false, operandType, model.GetTypeSymbol(testedType), model.Compilation);
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

    // x is T v and x is not T v: the test and the assignment of v in one expression, the operand evaluated once
    private static bool TryTranspileDeclaration(IsPatternExpressionSyntax node, DeclarationPatternSyntax declaration,
        SingleVariableDesignationSyntax designation, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        if (TypeTests.PlanDeclaration(declaration, model) is not var (plan, owner, negated) ||
            owner != node ||
            model.GetDeclaredSymbol<ILocalSymbol>(designation) is not { } local ||
            model.GetTypeSymbol(node.Expression) is not { } operandType)
        {
            return false;
        }

        TypeTests.AppendDeclaration(in plan, negated, node.Expression, designation, local.Type, operandType,
            ref context);
        return true;
    }

    // A type pattern or a declaration pattern, also under not
    private static bool HasTypeTest(PatternSyntax pattern) =>
        pattern is TypePatternSyntax or DeclarationPatternSyntax or
            UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern, Pattern: TypePatternSyntax or DeclarationPatternSyntax };
}
