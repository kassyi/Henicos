namespace Kassyi.GSharp.Compiler.Models;

/// <summary>
/// Where a <c>ref</c> local is declared that the CPU state machine would keep in its frame and GSH0039 does not report
/// (<see cref="HeldRefLocal"/> without <see cref="HeldRefLocal.IsReportedLive"/>): the generator reports GSH0003 there
/// on a CPU dialect build (#1640).
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
internal readonly record struct HeldRefLocalSite(string Path, int TreeOrdinal, TextSpan Span, LinePositionSpan LineSpan)
{
    /// <summary>
    /// The sites of the held ref locals of <paramref name="lowered"/> that GSH0039 does not report, in the order of the
    /// frame; empty for a body without a state machine.
    /// </summary>
    public static EquatableArray<HeldRefLocalSite> Collect(LoweredStateMachine? lowered, SemanticModel semanticModel)
    {
        if (lowered is null || lowered.HeldRefLocals.IsEmpty)
        {
            return default;
        }

        var sites = ImmutableArray.CreateBuilder<HeldRefLocalSite>();
        foreach (var held in lowered.HeldRefLocals)
        {
            if (!held.IsReportedLive)
            {
                sites.Add(Create(semanticModel.GetReportLocation(held.Declarator), semanticModel.Compilation));
            }
        }

        return sites.ToImmutable().AsEquatableArray();
    }

    /// <summary>
    /// Reports GSH0003 (<see cref="GpuCodeConstraints.RefLocalHeldByCpuStateMachine"/>) at this site: in the syntax
    /// tree of <paramref name="compilation"/> that declares it, so that the editor marks the declaration.
    /// </summary>
    public void Report(SourceProductionContext context, Compilation compilation)
    {
        var location = FindTree(compilation) is { } tree
            ? Location.Create(tree, Span)
            : Path.Length > 0
                ? Location.Create(Path, Span, LineSpan)
                : Location.None;
        context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.UnsupportedSyntax, location,
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
