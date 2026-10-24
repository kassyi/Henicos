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
                    context.Emitter.Writer.Append($"auto {GSharpInternalNames.Lowering.TupleTempPrefix}{i} = ");
                    context.Visitor.Visit(rightTuple.Arguments[i].Expression);
                    context.Emitter.Writer.AppendLine(";");
                }

                for (var i = 0; i < leftTuple.Arguments.Count; i++)
                {
                    context.Visitor.Visit(leftTuple.Arguments[i].Expression);
                    context.Emitter.Writer
                        .Append($" = {GSharpInternalNames.Lowering.TupleTempPrefix}{i}")
                        .AppendLine(";");
                }
            }

            return;
        }

        if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            NullRepresentation.AppendCoalesceAssignment(node.Left, node.Right, ref context);
            return;
        }

        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            ComputedPropertyGetterCall.TranspileAssignment(node, node.OperatorToken.Text, ref context);
            return;
        }

        if (ComputedPropertyGetterCall.IsComputedPropertyTarget(node.Left, ref context))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node,
                ComputedPropertyGetterCall.CompoundAssignmentToComputedProperty);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        context.Visitor.Visit(node.Left);
        context.Emitter.Writer.Append(node.OperatorToken.Text);
        context.Visitor.Visit(node.Right);
    }
}
