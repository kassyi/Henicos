#nullable enable
using System.Text;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>The value one path wrote to a mismatching cell.</summary>
/// <param name="Path">The path.</param>
/// <param name="Value">The value, formatted with its bits.</param>
/// <param name="Differs">Whether it differs from the oracle.</param>
/// <param name="Known">The registry entry that covers the difference, or <see langword="null"/>.</param>
public sealed record PathCell(DiffPath Path, string Value, bool Differs, KnownDivergence? Known);

/// <summary>A cell where at least one path differs from the oracle.</summary>
/// <param name="Seed">The seed of the inputs.</param>
/// <param name="Buffer">The buffer name.</param>
/// <param name="Index">The cell index in the buffer.</param>
/// <param name="Row">The row (the thread that wrote the cell).</param>
/// <param name="Column">The column index.</param>
/// <param name="ColumnName">The column name.</param>
/// <param name="Inputs">The row's inputs, formatted.</param>
/// <param name="Expected">The oracle's value, formatted.</param>
/// <param name="Paths">The value of every path.</param>
public sealed record CellMismatch(
    int Seed,
    string Buffer,
    int Index,
    int Row,
    int Column,
    string ColumnName,
    string Inputs,
    string Expected,
    IReadOnlyList<PathCell> Paths
)
{
    /// <summary>Gets a value indicating whether every differing path is covered by a <see cref="KnownDivergence"/>.</summary>
    public bool IsKnown => Paths.All(static p => !p.Differs || p.Known is not null);
}

/// <summary>A path that threw instead of finishing the launch; it counts as a divergence.</summary>
/// <param name="Seed">The seed.</param>
/// <param name="Path">The path.</param>
/// <param name="Exception">What it threw.</param>
public sealed record PathFailure(int Seed, DiffPath Path, Exception Exception);

/// <summary>A <see cref="KnownDivergence"/> that no longer differs on one path for any seed.</summary>
/// <param name="Entry">The entry.</param>
/// <param name="Path">The path on which the column now matches the oracle.</param>
public sealed record StaleDivergence(KnownDivergence Entry, DiffPath Path);

/// <summary>
/// The outcome of <see cref="DifferentialRunner.Evaluate"/>: every mismatching cell for every seed, the paths that threw,
/// and the registry entries that no longer reproduce. <see cref="ToString"/> is the failure message (design §4.4).
/// </summary>
public sealed class DifferentialReport
{
    // At most this many cells are printed per seed; all are counted
    private const int CellsShownPerSeed = 5;

    private readonly List<CellMismatch> _mismatches = [];
    private readonly List<PathFailure> _failures = [];
    private readonly List<StaleDivergence> _stale = [];

    internal DifferentialReport(DifferentialCase testCase, IReadOnlyList<DiffPathRunner> paths, IReadOnlyList<int> seeds, string? testSeed)
    {
        Case = testCase;
        Paths = paths;
        Seeds = seeds;
        TestSeed = testSeed;
    }

    /// <summary>Gets the case.</summary>
    public DifferentialCase Case { get; }

    /// <summary>Gets the paths that ran.</summary>
    public IReadOnlyList<DiffPathRunner> Paths { get; }

    /// <summary>Gets the seeds that ran.</summary>
    public IReadOnlyList<int> Seeds { get; }

    /// <summary>Gets the <c>TEST_SEED</c> the seeds were derived from, or <see langword="null"/>.</summary>
    public string? TestSeed { get; }

    /// <summary>Gets every mismatching cell, known or not.</summary>
    public IReadOnlyList<CellMismatch> Mismatches => _mismatches;

    /// <summary>Gets the mismatching cells that no <see cref="KnownDivergence"/> covers.</summary>
    public IReadOnlyList<CellMismatch> UnexpectedMismatches => _mismatches.Where(static m => !m.IsKnown).ToList();

    /// <summary>Gets the paths that threw.</summary>
    public IReadOnlyList<PathFailure> Failures => _failures;

    /// <summary>Gets the registry entries that no longer reproduce.</summary>
    public IReadOnlyList<StaleDivergence> StaleDivergences => _stale;

    /// <summary>Gets a value indicating whether the test must fail.</summary>
    public bool HasFailures => _failures.Count > 0 || _stale.Count > 0 || _mismatches.Any(static m => !m.IsKnown);

    internal void Add(CellMismatch mismatch) => _mismatches.Add(mismatch);

    internal void Add(PathFailure failure) => _failures.Add(failure);

    internal void Add(StaleDivergence stale) => _stale.Add(stale);

    /// <inheritdoc/>
    public override string ToString()
    {
        var text = new StringBuilder();
        string oracle = Case.Reference is null ? "sequential C#" : "reference model";
        text.AppendLine($"Differential mismatch in {Case.Name} (oracle: {oracle})");
        text.AppendLine($"  launch {Case.Shape}, paths: {string.Join(' ', Paths.Select(static p => p.Name))}");
        if (TestSeed is not null)
        {
            text.AppendLine($"  {DifferentialSeeds.TestSeedVariable}={TestSeed} added seed {DifferentialSeeds.Derive(TestSeed)}");
        }

        int width = Paths.Select(static p => p.Name.Length).Append("oracle".Length).Max();
        var failingSeeds = new SortedSet<int>();
        foreach (int seed in Seeds)
        {
            var cells = _mismatches.Where(m => m.Seed == seed && !m.IsKnown).ToList();
            var failures = _failures.Where(f => f.Seed == seed).ToList();
            if (cells.Count == 0 && failures.Count == 0)
            {
                continue;
            }

            failingSeeds.Add(seed);
            foreach (var failure in failures)
            {
                var ex = failure.Exception;
                text.AppendLine($"  seed {SeedLabel(seed)}: {Name(failure.Path)} threw {ex.GetType().FullName}: {ex.Message}");
            }

            if (cells.Count == 0)
            {
                continue;
            }

            text.AppendLine($"  seed {SeedLabel(seed)}, {cells.Count} mismatching cell{(cells.Count == 1 ? "" : "s")}; first:");
            foreach (var cell in cells.Take(CellsShownPerSeed))
            {
                text.AppendLine($"    {cell.Buffer}[{cell.Index}] thread {cell.Row}, column {cell.Column} \"{cell.ColumnName}\"");
                if (cell.Inputs.Length > 0)
                {
                    text.AppendLine($"      inputs: {cell.Inputs}");
                }

                text.AppendLine($"      {"oracle".PadRight(width)}: {cell.Expected}");
                foreach (var path in cell.Paths)
                {
                    string mark = !path.Differs ? "" : path.Known is { } entry ? $"    <- differs (known, #{entry.Issue})" : "    <- differs";
                    text.AppendLine($"      {Name(path.Path).PadRight(width)}: {path.Value}{mark}");
                }
            }
        }

        var unexpected = _mismatches.Where(static m => !m.IsKnown).ToList();
        if (unexpected.Count > 0)
        {
            var summary = unexpected
                .GroupBy(static m => m.ColumnName)
                .Select(g => $"\"{g.Key}\" {g.Count()} cell{(g.Count() == 1 ? "" : "s")} ({string.Join(' ', DifferingPaths(g))})");
            text.AppendLine($"  column summary: {string.Join(", ", summary)}");
        }

        foreach (var stale in _stale)
        {
            text.AppendLine(
                $"  known divergence {stale.Entry} no longer differs on {Name(stale.Path)} for any seed: " +
                $"remove it from {nameof(KnownDivergences)} (or narrow its paths) and close #{stale.Entry.Issue} if it is fixed"
            );
        }

        var known = _mismatches.Where(static m => m.IsKnown).ToList();
        if (known.Count > 0)
        {
            text.AppendLine($"  known divergences seen: {known.Count} cell{(known.Count == 1 ? "" : "s")} in {string.Join(", ", known.Select(static m => $"\"{m.ColumnName}\"").Distinct())}");
        }

        if (failingSeeds.Count > 0)
        {
            text.AppendLine($"  reproduce: DifferentialRunner.Run({Case.Name}, seeds: [{string.Join(", ", failingSeeds)}])");
        }

        return text.ToString();
    }

    private static string Name(DiffPath path) => path.ToString().ToLowerInvariant();

    private static string SeedLabel(int seed) => seed == 0 ? "0 (boundary tables)" : seed.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static IEnumerable<string> DifferingPaths(IEnumerable<CellMismatch> cells) =>
        cells
            .SelectMany(static m => m.Paths)
            .Where(static p => p.Differs && p.Known is null)
            .Select(static p => Name(p.Path))
            .Distinct();
}
