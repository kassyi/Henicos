namespace Kassyi.GSharp.Compiler.Models;

/// <summary>
/// Where a <c>ref</c> local is declared that the CPU state machine would keep in its frame and GSH0039 does not report
/// (<see cref="HeldRefLocal"/> without <see cref="HeldRefLocal.IsReportedLive"/>): the generator reports GSH0003 there
/// on a CPU dialect build (#1640). Also where a span local carries a value from one state into another
/// (<see cref="LoweredStateMachine.SpansAcrossStates"/>), which the generator reports as GSH0039
/// (<see cref="SpanName"/>). Also where a goto jumps to the inner label of chained labels from another state
/// (<see cref="ChainedLabelJumpChecks.FindReported"/>), which the generator reports as GSH0121 (<see cref="Label"/>),
/// from the generator alone: the analyzer does not report it, as an IDE would show the same diagnostic of both twice.
/// </summary>
/// <remarks>
/// Values only (the path and the spans of the declaration), so a result taken from the cache never refers to an earlier
/// compilation.
/// </remarks>
/// <param name="Path">
/// The file of the declaration, or empty when its tree has no path or it has no source location.
/// </param>
/// <param name="TreeOrdinal">
/// The position of its tree in the compilation, which finds a tree without a path (one built in memory); -1 when it has
/// no source location.
/// </param>
/// <param name="Span">The span of the declared identifier.</param>
/// <param name="LineSpan">Its lines and columns.</param>
/// <param name="SpanName">The name of the span local for GSH0039, or empty for a ref local (GSH0003).</param>
/// <param name="Label">The inner label that the goto names, for GSH0121; empty otherwise.</param>
/// <param name="OuterLabel">The first label of the chain, for GSH0121; empty otherwise.</param>
/// <param name="CrossesBarrier">
/// Whether that goto crosses a barrier or a call that suspends, which makes it GSH0040 instead of GSH0121.
/// </param>
internal readonly record struct HeldRefLocalSite(string Path, int TreeOrdinal, TextSpan Span, LinePositionSpan LineSpan,
    string SpanName = "", string Label = "", string OuterLabel = "", bool CrossesBarrier = false)
{
    /// <summary>Whether this is a goto of GSH0121, which the managed state machines of exported device code need too.</summary>
    public bool IsChainedLabelGoto => Label.Length > 0;

    /// <summary>
    /// The sites of the held ref locals of <paramref name="lowered"/> that GSH0039 does not report, in the order of the
    /// frame, then the span locals across states and the gotos of GSH0121; empty for a body without a state machine.
    /// </summary>
    public static EquatableArray<HeldRefLocalSite> Collect(LoweredStateMachine? lowered, SemanticModel semanticModel,
        MethodDeclarationSyntax declaration)
    {
        if (lowered is null ||
            (lowered.HeldRefLocals.IsEmpty && lowered.SpansAcrossStates.IsEmpty &&
             lowered.GotosToChainedLabelsAcrossStates.IsEmpty))
        {
            return default;
        }

        var sites = ImmutableArray.CreateBuilder<HeldRefLocalSite>();
        foreach (var held in lowered.HeldRefLocals)
        {
            if (!held.IsReportedLive)
            {
                sites.Add(Create(held.Declarator, semanticModel));
            }
        }

        foreach (var span in lowered.SpansAcrossStates)
        {
            sites.Add(Create(span, semanticModel) with
            {
                SpanName = span.Identifier.ValueText
            });
        }

        foreach (var (location, jump, crossesBarrier) in
                 ChainedLabelJumpChecks.FindReported(declaration, semanticModel, lowered))
        {
            sites.Add(Create(location, semanticModel.Compilation) with
            {
                Label = jump.Label,
                OuterLabel = jump.OuterLabel,
                CrossesBarrier = crossesBarrier
            });
        }

        return sites.ToImmutable().AsEquatableArray();
    }

    /// <summary>
    /// Reports GSH0003 (<see cref="GpuCodeConstraints.RefLocalHeldByCpuStateMachine"/>), GSH0039 for a span local, or
    /// GSH0121 (GSH0040 across a barrier) for a goto, at this site: in the syntax tree of <paramref name="compilation"/> that holds it, so that the
    /// editor marks it.
    /// </summary>
    public void Report(SourceProductionContext context, Compilation compilation)
    {
        var location = FindTree(compilation) is { } tree
            ? Location.Create(tree, Span)
            : Path.Length > 0
                ? Location.Create(Path, Span, LineSpan)
                : Location.None;
        context.ReportDiagnostic(IsChainedLabelGoto
            ? CrossesBarrier
                ? Diagnostic.Create(GSharpDiagnostics.CrossBarrierGotoProhibited, location)
                : Diagnostic.Create(GSharpDiagnostics.GotoToChainedLabelAcrossStates, location, Label, OuterLabel)
            : SpanName.Length > 0
                ? Diagnostic.Create(GSharpDiagnostics.CrossBarrierRefStructProhibited, location, SpanName)
                : Diagnostic.Create(GSharpDiagnostics.UnsupportedSyntax, location,
                    GpuCodeConstraints.RefLocalHeldByCpuStateMachine));
    }

    // The tree with the site's path, or at its ordinal when it has no path
    private SyntaxTree? FindTree(Compilation compilation)
    {
        var trees = SemanticModelExtensions.GetSyntaxTrees(compilation);
        if (Path.Length == 0)
        {
            return TreeOrdinal >= 0 && TreeOrdinal < trees.Length && trees[TreeOrdinal].FilePath.Length == 0
                ? trees[TreeOrdinal]
                : null;
        }

        return trees
            .AsStackEnumerable()
            .FirstOrDefault(Path, static (tree, path) => tree.FilePath == path);
    }

    // The site of <node> at its report location. A node of a closed copy of a generic declaration reports in the user's
    // tree, which the derived compilation of the copy does not hold: the ordinal is that of the copy's tree, which
    // replaced the user's tree at the same position, so that a tree without a path is still found
    private static HeldRefLocalSite Create(SyntaxNode node, SemanticModel semanticModel)
    {
        var site = Create(semanticModel.GetReportLocation(node), semanticModel.Compilation);
        return site.TreeOrdinal < 0 && site.Path.Length == 0 &&
               semanticModel.GetCompilationNode(node) is { SyntaxTree: var tree }
            ? site with { TreeOrdinal = SemanticModelExtensions.GetSyntaxTreeOrdinal(semanticModel.Compilation, tree) }
            : site;
    }

    // The site of the declared identifier, as GSH0039 reports it: location is a declarator (<name> = <initializer>), or
    // the foreach statement whose iteration variable ForEachRewriter declared; otherwise the whole location
    private static HeldRefLocalSite Create(Location location, Compilation compilation)
    {
        if (location.SourceTree is not { } tree)
        {
            return new HeldRefLocalSite("", -1, default, default);
        }

        var span = tree.GetRoot().FindNode(location.SourceSpan) switch
        {
            VariableDeclaratorSyntax declarator => declarator.Identifier.Span,
            ForEachStatementSyntax forEach => forEach.Identifier.Span,
            _ => location.SourceSpan
        };
        var lineSpan = tree.GetLineSpan(span);
        var ordinal = SemanticModelExtensions.GetSyntaxTreeOrdinal(compilation, tree);
        return new HeldRefLocalSite(lineSpan.Path, ordinal, span, lineSpan.Span);
    }
}
