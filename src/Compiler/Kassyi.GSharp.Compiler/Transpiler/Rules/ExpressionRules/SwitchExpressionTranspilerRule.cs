namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles C# 8+ switch expressions (<c>expr switch { ... }</c>) into immediately invoked C++ lambdas containing <c>switch</c> statements.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class SwitchExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<SwitchExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="SwitchExpressionTranspilerRule"/>.
    /// </summary>
    public SwitchExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(SwitchExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Emitter.Writer.ImmediatelyInvokedLambda())
        {
            context.Emitter.Writer.Append("switch");
            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(node.GoverningExpression);
            }

            context.Emitter.Writer.AppendLine();
            using (context.Emitter.Writer.Scope())
            {
                foreach (var arm in node.Arms)
                {
                    if (arm.WhenClause != null)
                    {
                        context.Reporter.Report(GSharpDiagnostics.UnsupportedSyntax,
                            arm.WhenClause,
                            "Switch expression arms with 'when' clauses are not supported");
                        return;
                    }

                    switch (arm.Pattern)
                    {
                        case ConstantPatternSyntax constantPattern:
                            context.Emitter.Writer.Append("case ");
                            context.Visitor.Visit(constantPattern.Expression);
                            context.Emitter.Writer.Append(":");
                            break;
                        case DiscardPatternSyntax:
                            context.Emitter.Writer.Append("default:");
                            break;
                        default:
                            context.Reporter.Report(GSharpDiagnostics.UnsupportedSyntax,
                                arm.Pattern,
                                "Switch expression patterns other than constant and discard are not supported");
                            context.Emitter.Writer.Append("default:");
                            break;
                    }

                    context.Emitter.Writer.Append(" return ");
                    context.Visitor.Visit(arm.Expression);
                    context.Emitter.Writer.AppendLine(";");
                }
            }
        }
    }
}
