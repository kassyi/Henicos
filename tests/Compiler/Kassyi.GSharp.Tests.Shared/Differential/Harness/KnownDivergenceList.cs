#nullable enable
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>The content of <c>KnownDivergences.yaml</c>: the issues and the entries expanded to one per column.</summary>
/// <param name="Issues">The issue blocks in file order.</param>
/// <param name="Entries">The entries in file order.</param>
internal sealed record KnownDivergenceList(IReadOnlyList<KnownDivergenceIssue> Issues, IReadOnlyList<KnownDivergence> Entries);
