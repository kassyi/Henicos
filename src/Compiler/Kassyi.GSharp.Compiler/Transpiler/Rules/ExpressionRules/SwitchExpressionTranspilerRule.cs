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
    public static readonly SwitchExpressionTranspilerRule Instance = new();

    private SwitchExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(SwitchExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.ImmediatelyInvokedLambda())
        {
            context.Writer.AppendKeyword("switch");
            using (context.Writer.Parentheses())
            {
                context.Visitor.Visit(node.GoverningExpression);
            }

            context.Writer.AppendLine();
            using (context.Writer.Scope())
            {
                foreach (var arm in node.Arms)
                {
                    if (arm.WhenClause != null)
                    {
                        context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.UnsupportedSyntax,
                            arm.WhenClause.GetLocation(),
                            "Switch expression arms with 'when' clauses are not supported"));
                        return;
                    }

                    switch (arm.Pattern)
                    {
                        case ConstantPatternSyntax constantPattern:
                            context.Writer.Append("case ");
                            context.Visitor.Visit(constantPattern.Expression);
                            context.Writer.Append(":");
                            break;
                        case DiscardPatternSyntax:
                            context.Writer.Append("default:");
                            break;
                        default:
                            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.UnsupportedSyntax,
                                arm.Pattern.GetLocation(),
                                "Switch expression patterns other than constant and discard are not supported"));
                            context.Writer.Append("default:");
                            break;
                    }

                    context.Writer.Append(" return ");
                    context.Visitor.Visit(arm.Expression);
                    context.Writer.AppendSemiColonLine();
                }
            }
        }
    }
}
