using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>The checked contents of a smells file.</summary>
/// <param name="DefaultExclude">Globs that exclude a file from every rule; they are tested first.</param>
/// <param name="Rules">The rules in file order.</param>
internal sealed record SmellConfig(ImmutableArray<string> DefaultExclude, ImmutableArray<SmellRule> Rules);
