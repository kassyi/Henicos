namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.Macros;

/// <summary>Checks <c>Gpu.InlinePtx</c>: the dialect supports inline PTX, and the text is a constant string.</summary>
internal static class InlinePtxChecks
{
    /// <summary>Reports a dialect without inline PTX and a PTX text that is not a constant string.</summary>
    public static void CheckInlinePtx(InvocationExpressionSyntax invocation, ref ValidationContext context)
    {
        if (!DialectCallSupport.SupportsInlinePtx(context.Dialect))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.InlinePtxNotSupportedByDialect,
                invocation,
                context.Dialect.Name);
        }

        // [WHY] Reported for every dialect: the text must be a constant even where the call becomes a runtime failure
        var args = invocation.ArgumentList.Arguments;
        if (args.Count >= 1 && !GpuCodeConstraints.IsInlinePtxText(args[0].Expression, context.SemanticModel))
        {
            context.ReportDiagnostic(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral, args[0].Expression);
        }
    }
}
