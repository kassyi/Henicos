namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>One place where a copy differs from its declaration.</summary>
/// <param name="Location">The node of the declaration, in the original compilation.</param>
/// <param name="Reason">What differs.</param>
internal sealed record CopyFinding(Location Location, string Reason);
