namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// An opaque body that <see cref="LivenessRecorder"/> is inside: the locals it captures and whether it makes a
/// delegate.
/// </summary>
/// <param name="body">The syntax of the body.</param>
/// <param name="mode">How the body runs.</param>
/// <param name="carrier">The index of the carrier local that receives the delegate, or -1.</param>
internal sealed class LivenessBodyFrame(SyntaxNode body, LivenessBodyMode mode, int carrier)
{
    /// <summary>The syntax of the body.</summary>
    public SyntaxNode Body { get; } = body;

    /// <summary>How the body runs.</summary>
    public LivenessBodyMode Mode { get; } = mode;

    /// <summary>The index of the carrier local that receives the delegate, or -1.</summary>
    public int Carrier { get; } = carrier;

    /// <summary>The indices of the locals declared outside the body that it uses.</summary>
    public HashSet<int> Captures { get; } = [];

    /// <summary>Whether a delegate is made inside the body.</summary>
    public bool MakesDelegate { get; set; }
}
