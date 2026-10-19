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
    public AssignmentExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        if (node is { Left: TupleExpressionSyntax leftTuple, Right: TupleExpressionSyntax rightTuple })
        {
            // [WHY] Emits an immediately invoked C++ lambda to evaluate RHS tuple elements before assigning to LHS, preventing variable swap clobbering.
            using (context.Emitter.Writer.ImmediatelyInvokedLambda())
            {
                for (var i = 0; i < rightTuple.Arguments.Count; i++)
                {
                    context.Emitter.Writer.Append($"auto {GSharpInternalNames.TupleTempPrefix}{i} = ");
                    context.Visitor.Visit(rightTuple.Arguments[i].Expression);
                    context.Emitter.Writer.AppendSemiColonLine();
                }

                for (var i = 0; i < leftTuple.Arguments.Count; i++)
                {
                    context.Visitor.Visit(leftTuple.Arguments[i].Expression);
                    context.Emitter.Writer
                        .Append($" = {GSharpInternalNames.TupleTempPrefix}{i}")
                        .AppendSemiColonLine();
                }
            }

            return;
        }

        if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            using (context.Emitter.Writer.ImmediatelyInvokedLambda("decltype(auto)"))
            {
                context.Emitter.Writer.Append($"auto& {GSharpInternalNames.CoalesceTarget} = ");
                context.Visitor.Visit(node.Left);
                context.Emitter.Writer.AppendSemiColonLine();

                context.Emitter.Writer.Append($"return !{GSharpInternalNames.CoalesceTarget}.HasValue ? ");
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Emitter.Writer.Append($"{GSharpInternalNames.CoalesceTarget} = ");
                    context.Visitor.Visit(node.Right);
                }

                context.Emitter.Writer.AppendLine($" : {GSharpInternalNames.CoalesceTarget}.Value;");
            }

            return;
        }

        context.Visitor.Visit(node.Left);
        context.Emitter.Writer.AppendOperator(node.OperatorToken.Text);
        context.Visitor.Visit(node.Right);
    }
}
