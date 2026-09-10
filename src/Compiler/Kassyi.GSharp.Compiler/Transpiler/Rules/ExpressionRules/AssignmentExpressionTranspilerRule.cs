namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class AssignmentExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<AssignmentExpressionSyntax>
{
    public static readonly AssignmentExpressionTranspilerRule Instance = new();

    private AssignmentExpressionTranspilerRule() { }

    public override void Transpile(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        if (node is { Left: TupleExpressionSyntax leftTuple, Right: TupleExpressionSyntax rightTuple })
        {
            // [WHY] Emits an immediately invoked C++ lambda to evaluate RHS tuple elements before assigning to LHS, preventing variable swap clobbering.
            using (context.Writer.Brackets())
            {
                context.Writer.Append("&");
            }

            context.Writer.AppendParentheses();
            using (context.Writer.Scope())
            {
                for (var i = 0; i < rightTuple.Arguments.Count; i++)
                {
                    context.Writer.Append($"auto _t{i} = ");
                    context.Visitor.Visit(rightTuple.Arguments[i].Expression);
                    context.Writer.AppendSemiColonLine();
                }

                for (var i = 0; i < leftTuple.Arguments.Count; i++)
                {
                    context.Visitor.Visit(leftTuple.Arguments[i].Expression);
                    context.Writer
                        .Append($" = _t{i}")
                        .AppendSemiColonLine();
                }
            }

            context.Writer.AppendParentheses();
            return;
        }

        if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            using (context.Writer.Brackets())
            {
                context.Writer.Append("&");
            }

            context.Writer.Append("() -> decltype(auto) ");
            using (context.Writer.Scope())
            {
                context.Writer.Append("auto& __coalesce_target = ");
                context.Visitor.Visit(node.Left);
                context.Writer.AppendSemiColonLine();

                context.Writer.Append("return !__coalesce_target.HasValue ? ");
                using (context.Writer.Parentheses())
                {
                    context.Writer.Append("__coalesce_target = ");
                    context.Visitor.Visit(node.Right);
                }

                context.Writer.AppendLine(" : __coalesce_target.Value;");
            }

            context.Writer.AppendParentheses();

            return;
        }

        context.Visitor.Visit(node.Left);
        context.Writer.AppendOperator(node.OperatorToken.Text);
        context.Visitor.Visit(node.Right);
    }
}
