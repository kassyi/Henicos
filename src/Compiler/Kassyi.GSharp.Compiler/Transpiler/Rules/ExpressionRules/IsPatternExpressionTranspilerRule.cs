namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles pattern matching expressions (such as <c>x is null</c> or <c>x is not null</c>) for array-like and nullable array-like types.
/// </summary>
public sealed class IsPatternExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<IsPatternExpressionSyntax>
{
    public override void Transpile(IsPatternExpressionSyntax node, ref TranspileContext context)
    {
        var negated = node.Pattern is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern };
        var pattern = node.Pattern is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern } not
            ? not.Pattern
            : node.Pattern;
        if (TryTranspileEmptyPattern(node, pattern, negated, ref context) ||
            TryTranspileTypePattern(node, pattern, negated, ref context))
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

    // x is {} and x is not {}: not null for an array or a nullable
    private static bool TryTranspileEmptyPattern(IsPatternExpressionSyntax node, PatternSyntax pattern, bool negated,
        ref TranspileContext context)
    {
        if (pattern is not RecursivePatternSyntax
            {
                Type: null,
                PositionalPatternClause: null,
                Designation: null,
                PropertyPatternClause.Subpatterns.Count: 0
            } ||
            !NullRepresentation.IsNullComparable(context.Query.SemanticModel.GetTypeSymbol(node.Expression)))
        {
            return false;
        }

        NullRepresentation.AppendNullTest(node.Expression, isNull: negated, ref context);
        return true;
    }

    // x is T, x is T _ and x is not T of a type that the types decide (TypeTests), and x is T v with its variable
    private static bool TryTranspileTypePattern(IsPatternExpressionSyntax node, PatternSyntax pattern, bool negated,
        ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
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

        var plan = TypeTests.Plan(false, model.GetTypeSymbol(node.Expression), model.GetTypeSymbol(testedType),
            model.Compilation);
        if (negated)
        {
            plan = TypeTests.Negate(in plan);
        }

        return TypeTests.TryAppend(in plan, node.Expression, ref context);
    }

    // x is T v and x is not T v: the test and the assignment of v in one expression, the operand evaluated once
    private static bool TryTranspileDeclaration(IsPatternExpressionSyntax node, DeclarationPatternSyntax declaration,
        SingleVariableDesignationSyntax designation, ref TranspileContext context)
    {
        if (TypeTests.PlanDeclaration(declaration, context.Query.SemanticModel) is not var (plan, owner) ||
            owner != node)
        {
            return false;
        }

        TypeTests.AppendDeclaration(in plan, node.Expression, designation, ref context);
        return true;
    }

    // A type pattern or a declaration pattern, also under not
    private static bool HasTypeTest(PatternSyntax pattern) =>
        pattern is TypePatternSyntax or DeclarationPatternSyntax or
            UnaryPatternSyntax
            {
                RawKind: (int)SyntaxKind.NotPattern,
                Pattern: TypePatternSyntax or DeclarationPatternSyntax
            };
}
