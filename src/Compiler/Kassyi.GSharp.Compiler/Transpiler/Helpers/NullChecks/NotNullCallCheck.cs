namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

/// <summary>
/// The null check of a <c>Gpu.NotNull(&lt;array&gt;)</c> call: none when the array is proven
/// (<see cref="NonNullProof"/>), otherwise <c>__gsharp_array_not_null(&lt;array&gt;, &lt;site&gt;)</c> with the place
/// of the call. A <c>Gpu.NotNull</c> call is itself proven, so a local initialized from it and never written is read
/// without a check.
/// </summary>
internal static class NotNullCallCheck
{
    /// <summary>
    /// The text of the error of <c>Gpu.NotNull</c> given a null array: the message of the C# method after <c>G#: </c>.
    /// </summary>
    public const string SiteMessage = "G#: Gpu.NotNull was given a null array";

    // The trap kind of the site ID, which tells the site apart from another trap at the same place
    private const string TrapKind = "ArrayNotNull";

    /// <summary>
    /// The check of the call <c>Gpu.NotNull(&lt;array&gt;)</c> (<paramref name="call"/>): none when
    /// <paramref name="array"/> is proven, otherwise the place of the call to report.
    /// </summary>
    public static NotNullCheck Plan(InvocationExpressionSyntax call, ExpressionSyntax array,
        ref TranspileContext context) =>
        Plan(call, array, context.Query.SemanticModel, context.Emitter.ErrorSites, context.Emitter.CurrentMemberName);

    /// <summary>
    /// <see cref="Plan(InvocationExpressionSyntax, ExpressionSyntax, ref TranspileContext)"/> with the parts of the
    /// context it reads: the model, the error site table of the method (<see langword="null"/> in a device function,
    /// whose site is a message that names the place) and the name of the method being emitted.
    /// </summary>
    public static NotNullCheck Plan(InvocationExpressionSyntax call, ExpressionSyntax array, SemanticModel model,
        ErrorSiteTable? table, string? currentMemberName) =>
        NonNullProof.IsProvenNonNull(array, model)
            ? new NotNullCheck(array, null)
            : new NotNullCheck(array,
                ArrayNullCheck.PlanSite(call, null, SiteMessage, TrapKind, model, table, currentMemberName));

    /// <summary>
    /// Writes the plan of <see cref="Plan(InvocationExpressionSyntax, ExpressionSyntax, ref TranspileContext)"/>:
    /// <c>__gsharp_array_not_null(&lt;array&gt;, &lt;site&gt;)</c>, or <c>(&lt;array&gt;)</c> when the array is proven.
    /// </summary>
    public static void Append(in NotNullCheck check, ref TranspileContext context)
    {
        if (check.Site is { } site)
        {
            ArrayNullCheck.AppendHelperCall(GSharpInternalNames.Prelude.ArrayNotNull, check.Array, site, ref context);
            return;
        }

        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(check.Array);
        }
    }
}
