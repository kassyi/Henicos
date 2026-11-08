namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// How an opaque body that <see cref="LivenessRecorder"/> enters runs (<see cref="LivenessBodyFrame"/>).
/// </summary>
internal enum LivenessBodyMode
{
    /// <summary>Runs where it is spliced.</summary>
    Run,

    /// <summary>A delegate stored in a carrier local.</summary>
    Carried,

    /// <summary>A delegate that may run after any later split.</summary>
    Delegate
}
