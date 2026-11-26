using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

/// <summary>
/// The <c>GpuBigIntExpr</c> locals of one operation block (GSH0005): which ones a declaration names, which ones keep
/// their value (<see cref="GpuBigIntExprUses"/>), and which locals a copy goes into.
/// </summary>
internal sealed class GpuBigIntExprLocals(GpuBigIntExprTypes types)
{
    private readonly HashSet<ILocalSymbol> _tracked = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ILocalSymbol> _kept = new(SymbolEqualityComparer.Default);
    private readonly Queue<ILocalSymbol> _newlyKept = new();
    private readonly Dictionary<ILocalSymbol, List<ILocalSymbol>> _copiedFrom = new(SymbolEqualityComparer.Default);
    private readonly List<(ILocalSymbol Local, Location Location)> _declared = [];

    /// <summary>The <c>GpuBigIntExpr</c> types of the compilation.</summary>
    public GpuBigIntExprTypes Types => types;

    /// <summary>Whether <paramref name="local"/> is a <c>GpuBigIntExpr</c> local of the block.</summary>
    public bool IsTracked(ILocalSymbol local) => _tracked.Contains(local);

    /// <summary>Whether <paramref name="local"/> keeps its value: it is evaluated, disposed or passed on.</summary>
    public bool IsKept(ILocalSymbol local) => _kept.Contains(local);

    /// <summary>Records <paramref name="local"/>; false when it was recorded already.</summary>
    public bool Track(ILocalSymbol local) => _tracked.Add(local);

    /// <summary>Records <paramref name="local"/> as declared at <paramref name="location"/>, where GSH0005 goes.
    /// </summary>
    public void Declare(ILocalSymbol local, Location location) => _declared.Add((local, location));

    /// <summary>Records that <paramref name="local"/> keeps its value.</summary>
    public void Keep(ILocalSymbol local)
    {
        if (_kept.Add(local))
        {
            _newlyKept.Enqueue(local);
        }
    }

    /// <summary>Records that <paramref name="source"/> is copied into <paramref name="target"/>.</summary>
    public void AddCopy(ILocalSymbol target, ILocalSymbol source)
    {
        if (!_copiedFrom.TryGetValue(target, out var sources))
        {
            sources = [];
            _copiedFrom.Add(target, sources);
        }

        sources.Add(source);
    }

    /// <summary>
    /// Marks every local whose value is copied into a kept local as kept. A local kept on one path is kept: the rule is
    /// for the obvious discard, and a flow analysis would report correct code (a form the analysis can only suspect is
    /// not an error).
    /// </summary>
    public void PropagateKept()
    {
        while (_newlyKept.Count > 0)
        {
            if (!_copiedFrom.TryGetValue(_newlyKept.Dequeue(), out var sources))
            {
                continue;
            }

            foreach (var source in sources)
            {
                Keep(source);
            }
        }
    }

    /// <summary>Reports GSH0005 at each declared local that does not keep its value.</summary>
    public void ReportDropped(OperationBlockAnalysisContext context)
    {
        foreach (var (local, location) in _declared)
        {
            if (!_kept.Contains(local))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GSharpDiagnostics.GpuBigIntExprDirectAssignment,
                    location,
                    local.Name));
            }
        }
    }
}
