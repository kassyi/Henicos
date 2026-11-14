namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>A reason why a node of the declaration has no faithful closed copy.</summary>
/// <param name="NodeIndex">The position of the node in <see cref="CopyNodeIndex.List"/> of the declaration.</param>
/// <param name="Reason">What differs or cannot be written, for the message.</param>
internal sealed record CopyProblem(int NodeIndex, string Reason);
