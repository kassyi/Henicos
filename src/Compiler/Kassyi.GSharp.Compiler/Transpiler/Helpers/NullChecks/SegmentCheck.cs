namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

/// <summary>
/// The check of the source of a <c>foreach</c> over an <c>ArraySegment&lt;T&gt;</c> (#1828): C# enters the loop over a
/// default segment, which has no array, with <see cref="InvalidOperationException"/>. The source goes through the
/// Prelude helper <c>__gsharp_segment_ref(&lt;source&gt;, &lt;site&gt;)</c>, which fails on every build; the site is
/// planned as for the null check of an array. The lowered <c>foreach</c> carries the same check in an annotation
/// (<see cref="ArrayNullCheckEmitter.TryAppendAnnotatedCheck"/>).
/// </summary>
internal static class SegmentCheck
{
    /// <summary>The text of the error of a default <c>ArraySegment&lt;T&gt;</c>, which .NET words this way.</summary>
    public const string SiteMessage = "The underlying array is null.";

    /// <summary>The trap kind of the site ID, which tells the site apart from another trap at the same place.</summary>
    public const string TrapKind = "SegmentNull";

    /// <summary>
    /// Writes the source <paramref name="receiver"/> of a <c>foreach</c>: an <c>ArraySegment&lt;T&gt;</c> through
    /// <c>__gsharp_segment_ref(&lt;receiver&gt;, &lt;site&gt;)</c>, as C# enters the loop over a default one with
    /// <see cref="InvalidOperationException"/>; any other source as <see cref="ArrayNullCheckEmitter.AppendReceiver"/> writes
    /// it.
    /// </summary>
    public static void AppendForEachSource(ExpressionSyntax receiver, ref TranspileContext context)
    {
        if (!ArrayViewMethods.IsArraySegment(context.Query.SemanticModel.GetTypeSymbol(receiver)))
        {
            ArrayNullCheckEmitter.AppendReceiver(receiver, ref context);
            return;
        }

        var site = ArrayNullCheckEmitter.PlanSite(receiver, null, SiteMessage, TrapKind, ref context);
        ArrayNullCheckEmitter.AppendHelperCall(GSharpInternalNames.Prelude.SegmentRef, receiver, site, ref context);
    }
}
