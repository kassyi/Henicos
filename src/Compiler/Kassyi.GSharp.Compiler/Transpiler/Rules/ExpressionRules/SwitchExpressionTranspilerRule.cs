namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles C# 8+ switch expressions (<c>expr switch { ... }</c>) into immediately invoked C++ lambdas containing <c>switch</c> statements.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// <para>
/// C# throws <see cref="System.Runtime.CompilerServices.SwitchExpressionException"/> (an
/// <see cref="InvalidOperationException"/>) when no arm matches. Without a discard arm the lambda ends with a trap that
/// reports <c>InvalidOperation</c> with the place of the switch expression and does not return, so no path of the lambda
/// runs off its end:
/// <c>([&amp;]() { switch (&lt;input&gt;) { case &lt;value&gt;: return &lt;arm&gt;; } __gsharp_trap_abort(GSharpErrorCode::InvalidOperation, &lt;site&gt;); })()</c>.
/// </para>
/// </remarks>
public sealed class SwitchExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<SwitchExpressionSyntax>
{
    // The message of SwitchExpressionException, so the native, CUDA and managed paths report the same text
    private const string NoMatchMessage = "Non-exhaustive switch expression failed to match its input.";

    /// <summary>
    /// Gets the singleton instance of the <see cref="SwitchExpressionTranspilerRule"/>.
    /// </summary>
    public SwitchExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(SwitchExpressionSyntax node, ref TranspileContext context)
    {
        var noMatch = PlanNoMatchTrap(node, ref context);
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
                        context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax,
                            arm.WhenClause,
                            GpuCodeConstraints.SwitchArmWhenClauseNotSupported);
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
                            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax,
                                arm.Pattern,
                                GpuCodeConstraints.SwitchArmPatternNotSupported);
                            context.Emitter.Writer.Append("default:");
                            break;
                    }

                    context.Emitter.Writer.Append(" return ");
                    context.Visitor.Visit(arm.Expression);
                    context.Emitter.Writer.AppendLine(";");
                }
            }

            if (noMatch is { } trap)
            {
                AppendNoMatchTrap(trap, ref context);
            }
        }
    }

    // The trap after the switch: none when a discard arm matches every input; otherwise a site of the kernel's error site
    // table, or (in a device function, which has no table) a message that names the place
    private static NoMatchTrap? PlanNoMatchTrap(SwitchExpressionSyntax node, ref TranspileContext context)
    {
        var hasDiscardArm = node.Arms
            .AsStackEnumerable()
            .Any(static arm => arm.Pattern is DiscardPatternSyntax);
        if (hasDiscardArm)
        {
            return null;
        }

        var model = context.Query.SemanticModel;
        if (context.Emitter.ErrorSites is { } table)
        {
            var memberName = context.Emitter.CurrentMemberName ?? "";
            return new NoMatchTrap(table.Register(node, model, memberName, NoMatchMessage, "SwitchExpression"), "");
        }

        var place = SourcePlace.Format(model, node);
        return new NoMatchTrap(0u, $"Non-exhaustive switch expression failed to match its input at {place}");
    }

    // __gsharp_trap_abort(GSharpErrorCode::InvalidOperation, <site>u); or __gsharp_trap_abort(GSharpErrorCode::InvalidOperation, "<message>");
    private static void AppendNoMatchTrap(NoMatchTrap trap, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append($"{GSharpInternalNames.Prelude.TrapAbort}(GSharpErrorCode::InvalidOperation, ");
        if (trap.SiteId != 0u)
        {
            writer.Append($"{trap.SiteId}u");
        }
        else
        {
            CppStringLiteral.Append(writer, trap.Message);
        }

        writer.AppendLine(");");
    }

    // Either a registered error site (SiteId) or, when there is no table, a message with the place
    private readonly record struct NoMatchTrap(uint SiteId, string Message);
}
