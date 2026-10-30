#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// A divergence that is filed as an issue and not fixed yet: the cells of <paramref name="Column"/> in
/// <paramref name="Case"/> differ from the oracle on <paramref name="Paths"/>. A listed column may differ there without
/// failing the test, and it must still differ on every listed path that the process runs: once it matches for every seed,
/// the test fails and asks for the entry to be removed (XFAIL in LLVM lit). It is not a skip: the rest of the case is
/// still compared, and a fix cannot go unnoticed.
/// </summary>
/// <param name="Case">The <see cref="DifferentialCase.Name"/>, e.g. <c>IntegerMatrixKernels.Int32ArithmeticMatrixKernel</c>.</param>
/// <param name="Column">The column name (<see cref="DiffBuffer.ColumnNameOf"/>), e.g. <c>a&gt;&gt;&gt;b</c>.</param>
/// <param name="Paths">The paths on which the column differs; one or more flags.</param>
/// <param name="Issue">The issue that tracks the divergence.</param>
public sealed record KnownDivergence(string Case, string Column, DiffPath Paths, int Issue)
{
    /// <summary>Returns whether the entry covers a cell of <paramref name="column"/> in <paramref name="caseName"/> on <paramref name="path"/>.</summary>
    /// <param name="caseName">The case.</param>
    /// <param name="column">The column name.</param>
    /// <param name="path">The path.</param>
    public bool Covers(string caseName, string column, DiffPath path) =>
        Case == caseName && Column == column && (Paths & path) != 0;

    /// <inheritdoc/>
    public override string ToString() => $"\"{Column}\" on {Paths.ToString().ToLowerInvariant()} (#{Issue})";
}

/// <summary>
/// The registry of <see cref="KnownDivergence"/> entries that <see cref="DifferentialRunner.Run"/> applies to every case
/// (design §5, D2). A matrix test that finds a new divergence files an issue (M1 or M2) and adds a line here; the PR that
/// fixes it removes the line, since the entry then fails.
/// </summary>
public static class KnownDivergences
{
    /// <summary>Gets every registered entry.</summary>
    public static IReadOnlyList<KnownDivergence> All { get; } =
    [
        // new("IntegerMatrixKernels.Int32ArithmeticMatrixKernel", "a>>>b", DiffPath.Native | DiffPath.Cuda, Issue: 722),
    ];
}
