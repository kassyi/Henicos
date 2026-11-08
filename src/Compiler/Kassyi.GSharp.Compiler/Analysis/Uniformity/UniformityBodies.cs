namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The spliced bodies that <see cref="UniformityRecorder"/> is inside, innermost last, and the opaque ones among them,
/// which may run at any time.
/// </summary>
internal sealed class UniformityBodies
{
    // The bodies being built, innermost last
    private readonly ImmutableArray<SplicedBody>.Builder _bodies = ImmutableArray.CreateBuilder<SplicedBody>();

    // The bodies of the lambdas and local functions being built that may run at any time
    private readonly ImmutableArray<SyntaxNode>.Builder _opaque = ImmutableArray.CreateBuilder<SyntaxNode>();

    /// <summary>The innermost opaque body, which the events recorded in it carry; null outside every one.</summary>
    public SyntaxNode? Opaque => _opaque.Count > 0 ? _opaque[_opaque.Count - 1] : null;

    /// <summary>The innermost body, when the walk is inside one.</summary>
    public SplicedBody? Innermost => _bodies.Count > 0 ? _bodies[_bodies.Count - 1] : null;

    /// <summary>Enters <paramref name="body"/>.</summary>
    public void Push(SplicedBody body)
    {
        if (body.IsOpaque)
        {
            _opaque.Add(body.Syntax);
        }

        _bodies.Add(body);
    }

    /// <summary>Leaves <paramref name="body"/>, the innermost body.</summary>
    public void Pop(SplicedBody body)
    {
        _bodies.RemoveAt(_bodies.Count - 1);
        if (body.IsOpaque)
        {
            _opaque.RemoveAt(_opaque.Count - 1);
        }
    }
}
