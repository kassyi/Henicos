#nullable enable
using System;

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
/// <param name="Hosts">The hosts on which the column differs; an entry for other hosts is ignored, also by the stale check.</param>
/// <param name="ShapePredicate">Optional predicate restricting the divergence to specific launch shapes.</param>
public sealed record KnownDivergence(
    string Case,
    string Column,
    DiffPath Paths,
    int Issue,
    DiffHost Hosts = DiffHost.Any,
    Predicate<LaunchShape>? ShapePredicate = null
)
{
    /// <summary>
    /// Reserved <see cref="Column"/> token matching an <see cref="ExceptionPolicy"/> mismatch
    /// (for example, Managed threw <see cref="IndexOutOfRangeException"/> while Native returned normally).
    /// </summary>
    public const string ExceptionColumn = "$exception";

    /// <summary>Gets whether the entry applies to the host this process runs on.</summary>
    public bool AppliesToThisHost => (Hosts & (OperatingSystem.IsWindows() ? DiffHost.Windows : DiffHost.Unix)) != 0;

    /// <summary>Returns whether the entry covers a cell of <paramref name="column"/> in <paramref name="caseName"/> on <paramref name="path"/> for <paramref name="shape"/>.</summary>
    /// <param name="caseName">The case.</param>
    /// <param name="column">The column name.</param>
    /// <param name="path">The path.</param>
    /// <param name="shape">The launch shape.</param>
    public bool Covers(string caseName, string column, DiffPath path, LaunchShape shape) =>
        Case == caseName &&
        Column == column &&
        (Paths & path) != 0 &&
        (ShapePredicate is null || ShapePredicate(shape));

    /// <inheritdoc/>
    public override string ToString() => $"\"{Column}\" on {DiffPaths.Format(Paths)} (#{Issue})";
}
