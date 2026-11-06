#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>An issue that owns entries of <see cref="KnownDivergences"/>, with the title the file records for it.</summary>
/// <param name="Number">The issue number.</param>
/// <param name="Title">The issue title, as on GitHub.</param>
/// <param name="Line">The 1-based line of the issue block in <c>KnownDivergences.yaml</c>.</param>
internal sealed record KnownDivergenceIssue(int Number, string Title, int Line);
