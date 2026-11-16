namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

/// <summary>
/// Where a failed argument check of a BCL call (<see cref="ArgumentExceptionMessages"/>) reports, planned before the
/// call is written, as <see cref="ArrayNullCheck.PlanSite(ExpressionSyntax, string, string, string, ref TranspileContext)"/>
/// does for a null array: a site of the kernel's error site table, or, in a method without a table, the message itself
/// (the message is already the text the mapper knows, so no place is appended).
/// </summary>
internal static class ArgumentCheckSite
{
    /// <summary>The site of <paramref name="message"/> at <paramref name="node"/>, of the kind <paramref name="trapKind"/>.</summary>
    public static NullCheckSite Plan(
        InvocationExpressionSyntax node,
        string message,
        string trapKind,
        ref TranspileContext context) =>
        context.Emitter.ErrorSites is { } table
            ? new NullCheckSite(
                table.Register(
                    node,
                    context.Query.SemanticModel,
                    context.Emitter.CurrentMemberName ?? "",
                    message,
                    trapKind),
                "")
            : new NullCheckSite(0u, message);

    /// <summary>Writes <c>, &lt;site&gt;</c> for each of <paramref name="sites"/>.</summary>
    public static void Append(SourceWriter writer, ReadOnlySpan<NullCheckSite> sites)
    {
        foreach (var site in sites)
        {
            writer.Append(", ");
            ArrayNullCheck.AppendSite(writer, site);
        }
    }

    /// <summary>The three failures of one side of <c>Array.Copy</c>: length, index and range.</summary>
    public readonly record struct CopySide(NullCheckSite Length, NullCheckSite Index, NullCheckSite Range);

    /// <summary>The checks of the source side of <c>Array.Copy</c> (<paramref name="isSource"/>) or of the destination.</summary>
    public static CopySide PlanCopySide(
        InvocationExpressionSyntax node,
        bool isSource,
        ref TranspileContext context) =>
        new(
            Plan(node, ArgumentExceptionMessages.CopyLength, "Array.Copy.Length", ref context),
            Plan(
                node,
                isSource ? ArgumentExceptionMessages.CopySourceIndex : ArgumentExceptionMessages.CopyDestinationIndex,
                isSource ? "Array.Copy.SourceIndex" : "Array.Copy.DestinationIndex",
                ref context),
            Plan(
                node,
                isSource ? ArgumentExceptionMessages.CopySource : ArgumentExceptionMessages.CopyDestination,
                isSource ? "Array.Copy.Source" : "Array.Copy.Destination",
                ref context));

    /// <summary>Writes <c>, &lt;length&gt;, &lt;index&gt;, &lt;range&gt;</c> of <paramref name="side"/>.</summary>
    public static void Append(SourceWriter writer, in CopySide side) =>
        Append(writer, [side.Length, side.Index, side.Range]);
}
