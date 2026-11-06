using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>Parameter values that replace the rule's values for the files matching <paramref name="Files"/>.</summary>
/// <param name="Files">The globs of the files the override applies to.</param>
/// <param name="Parameters">The replaced values.</param>
internal sealed record ParameterOverride(ImmutableArray<string> Files, ImmutableDictionary<string, int> Parameters);
