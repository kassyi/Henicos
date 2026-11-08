namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The opaque bodies that <see cref="LivenessRecorder"/> is inside, innermost last, and the captures each carrier local
/// receives.
/// </summary>
internal sealed class LivenessFrames
{
    private readonly ImmutableArray<LivenessBodyFrame>.Builder _frames =
        ImmutableArray.CreateBuilder<LivenessBodyFrame>();

    private readonly Dictionary<int, HashSet<int>> _carrierCaptures = new();

    /// <summary>The number of opaque bodies the walk is inside.</summary>
    public int Count => _frames.Count;

    /// <summary>
    /// For each carrier that receives a delegate with captures, by index, the indices of the captured locals.
    /// </summary>
    public IReadOnlyDictionary<int, HashSet<int>> CarrierCaptures => _carrierCaptures;

    /// <summary>
    /// Enters an opaque body; a body that makes a delegate makes one in every body it is inside.
    /// </summary>
    public void Push(LivenessBodyFrame frame)
    {
        if (frame.Mode != LivenessBodyMode.Run)
        {
            foreach (var outer in _frames)
            {
                outer.MakesDelegate = true;
            }
        }

        _frames.Add(frame);
    }

    /// <summary>Leaves the innermost opaque body.</summary>
    public LivenessBodyFrame Pop()
    {
        var frame = _frames[_frames.Count - 1];
        _frames.RemoveAt(_frames.Count - 1);
        return frame;
    }

    /// <summary>The local <paramref name="index"/> is used inside every body the walk is inside.</summary>
    public void AddCapture(int index)
    {
        foreach (var frame in _frames)
        {
            frame.Captures.Add(index);
        }
    }

    /// <summary>The captures of <paramref name="frame"/> go to its carrier local.</summary>
    public void RecordCarriedCaptures(LivenessBodyFrame frame)
    {
        if (!_carrierCaptures.TryGetValue(frame.Carrier, out var captures))
        {
            captures = [];
            _carrierCaptures.Add(frame.Carrier, captures);
        }

        captures.UnionWith(frame.Captures);
    }

    /// <summary>Whether <paramref name="local"/> is declared inside any body the walk is inside.</summary>
    public bool IsDeclaredInFrame(ISymbol local)
    {
        foreach (var frame in _frames)
        {
            if (IsDeclaredIn(local, frame.Body))
            {
                return true;
            }
        }

        return false;
    }

    // Whether local is declared within the span of scope syntax
    private static bool IsDeclaredIn(ISymbol local, SyntaxNode syntax) =>
        local.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(syntax, static (reference, scope) =>
                reference.SyntaxTree == scope.SyntaxTree && scope.Span.Contains(reference.Span)
            );
}
