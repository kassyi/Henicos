namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles C# assignment expressions into target dialect statements,
/// supporting standard assignments, compound assignments, and deconstructing tuple assignments via immediately-invoked lambdas.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class AssignmentExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<AssignmentExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="AssignmentExpressionTranspilerRule"/>.
    /// </summary>
    public static readonly AssignmentExpressionTranspilerRule Instance = new();

    private AssignmentExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        if (node is { Left: TupleExpressionSyntax leftTuple, Right: TupleExpressionSyntax rightTuple })
        {
            // [WHY] Emits an immediately invoked C++ lambda to evaluate RHS tuple elements before assigning to LHS, preventing variable swap clobbering.
            using (context.Writer.ImmediatelyInvokedLambda())
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

            return;
        }

        if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            using (context.Writer.ImmediatelyInvokedLambda("decltype(auto)"))
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

            return;
        }

        context.Visitor.Visit(node.Left);
        context.Writer.AppendOperator(node.OperatorToken.Text);
        context.Visitor.Visit(node.Right);
    }
}
