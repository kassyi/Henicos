#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>The value one path wrote to a mismatching cell.</summary>
/// <param name="Path">The path.</param>
/// <param name="Value">The value, formatted with its bits.</param>
/// <param name="Differs">Whether it differs from the oracle.</param>
/// <param name="Known">The registry entry that covers the difference, or <see langword="null"/>.</param>
public sealed record PathCell(DiffPath Path, string Value, bool Differs, KnownDivergence? Known);

/// <summary>Identifies a cell in a <see cref="DiffBuffer"/> by its buffer name, flat index, column name, and column count per row.</summary>
/// <param name="Buffer">The buffer name.</param>
/// <param name="Index">The cell index in the buffer.</param>
/// <param name="ColumnName">The column name.</param>
/// <param name="ColumnCount">The number of columns per row (1 when the buffer has no named columns).</param>
public readonly record struct CellLocation(string Buffer, int Index, string ColumnName, int ColumnCount = 1)
{
    /// <summary>Gets the row (the thread that wrote the cell).</summary>
    public int Row => Index / Math.Max(1, ColumnCount);

    /// <summary>Gets the column index.</summary>
    public int Column => Index % Math.Max(1, ColumnCount);
}

/// <summary>A cell where at least one path differs from the oracle.</summary>
/// <param name="Seed">The seed of the inputs.</param>
/// <param name="Location">The cell location in the buffer.</param>
/// <param name="Inputs">The row's inputs, formatted.</param>
/// <param name="Expected">The oracle's value, formatted.</param>
/// <param name="Paths">The value of every path.</param>
/// <param name="Shape">The launch shape on which the mismatch occurred.</param>
public sealed record CellMismatch(
    int Seed,
    CellLocation Location,
    string Inputs,
    string Expected,
    IReadOnlyList<PathCell> Paths,
    LaunchShape? Shape = null
)
{
    /// <summary>Gets the buffer name.</summary>
    public string Buffer => Location.Buffer;

    /// <summary>Gets the cell index in the buffer.</summary>
    public int Index => Location.Index;

    /// <summary>Gets the row (the thread that wrote the cell).</summary>
    public int Row => Location.Row;

    /// <summary>Gets the column index.</summary>
    public int Column => Location.Column;

    /// <summary>Gets the column name.</summary>
    public string ColumnName => Location.ColumnName;

    /// <summary>Gets a value indicating whether every differing path is covered by a <see cref="KnownDivergence"/>.</summary>
    public bool IsKnown => Paths.All(static p => !p.Differs || p.Known is not null);
}

/// <summary>A path that threw instead of finishing the launch; it counts as a divergence.</summary>
/// <param name="Seed">The seed.</param>
/// <param name="Path">The path.</param>
/// <param name="Exception">What it threw.</param>
/// <param name="Shape">The launch shape on which the path threw.</param>
/// <param name="Known">The registry entry that covers the thrown exception, or <see langword="null"/>.</param>
public sealed record PathFailure(int Seed, DiffPath Path, Exception Exception, LaunchShape? Shape = null, KnownDivergence? Known = null);

/// <summary>A post-launch property or invariant check (<see cref="DifferentialCase.Verify"/> or <see cref="DifferentialCase.VerifyAgainstInitial"/>) that failed on one path.</summary>
/// <param name="Seed">The seed.</param>
/// <param name="Path">The path.</param>
/// <param name="Message">The failure message returned by the property check.</param>
/// <param name="Shape">The launch shape on which the property check failed.</param>
public sealed record PropertyFailure(int Seed, DiffPath Path, string Message, LaunchShape? Shape = null);

/// <summary>A <see cref="KnownDivergence"/> that no longer differs on one path for any seed.</summary>
/// <param name="Entry">The entry.</param>
/// <param name="Path">The path on which the column now matches the oracle.</param>
public sealed record StaleDivergence(KnownDivergence Entry, DiffPath Path);

/// <summary>
/// The outcome of <see cref="DifferentialRunner.Evaluate"/>: every mismatching cell for every seed, the paths that threw,
/// and the registry entries that no longer reproduce. <see cref="ToString"/> is the failure message.
/// </summary>
public sealed class DifferentialReport
{
    // At most this many cells are printed per seed; all are counted
    private const int CellsShownPerSeed = 5;

    private readonly List<CellMismatch> _mismatches = [];
    private readonly List<PathFailure> _failures = [];
    private readonly List<PropertyFailure> _propertyFailures = [];
    private readonly List<StaleDivergence> _stale = [];
    private readonly List<LaunchShape> _executedShapes = [];

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

    /// <summary>Gets the distinct launch shapes executed during evaluation.</summary>
    public IReadOnlyList<LaunchShape> ExecutedShapes => _executedShapes;

    /// <summary>Gets the <c>TEST_SEED</c> the seeds were derived from, or <see langword="null"/>.</summary>
    public string? TestSeed { get; }

    /// <summary>Gets every mismatching cell, known or not.</summary>
    public IReadOnlyList<CellMismatch> Mismatches => _mismatches;

    /// <summary>Gets the mismatching cells that no <see cref="KnownDivergence"/> covers.</summary>
    public IReadOnlyList<CellMismatch> UnexpectedMismatches => _mismatches.Where(static m => !m.IsKnown).ToList();

    /// <summary>Gets the paths that threw.</summary>
    public IReadOnlyList<PathFailure> Failures => _failures;

    /// <summary>Gets the post-launch property check failures.</summary>
    public IReadOnlyList<PropertyFailure> PropertyFailures => _propertyFailures;

    /// <summary>Gets the registry entries that no longer reproduce.</summary>
    public IReadOnlyList<StaleDivergence> StaleDivergences => _stale;

    /// <summary>Gets a value indicating whether the test must fail.</summary>
    public bool HasFailures =>
        _failures.Any(static f => f.Known is null) ||
        _propertyFailures.Count > 0 ||
        _stale.Count > 0 ||
        _mismatches.Any(static m => !m.IsKnown);

    internal void AddExecutedShape(LaunchShape shape)
    {
        if (!_executedShapes.Contains(shape))
        {
            _executedShapes.Add(shape);
        }
    }

    internal void Add(CellMismatch mismatch) => _mismatches.Add(mismatch);

    internal void Add(PathFailure failure) => _failures.Add(failure);

    internal void Add(PropertyFailure propertyFailure) => _propertyFailures.Add(propertyFailure);

    internal void Add(StaleDivergence stale) => _stale.Add(stale);

    /// <inheritdoc/>
    public override string ToString()
    {
        var text = new StringBuilder();
        string oracle = Case.Reference is null ? "sequential C#" : "reference model";
        text.AppendLine($"Differential mismatch in {Case.Name} (oracle: {oracle})");
        string shapeSummary = _executedShapes.Count <= 1
            ? Case.Shape.ToString()
            : string.Join("; ", _executedShapes.Select(static s => s.ToString()));
        text.AppendLine($"  launch {shapeSummary}, paths: {string.Join(' ', Paths.Select(static p => p.Name))}");
        if (TestSeed is not null)
        {
            text.AppendLine($"  {DifferentialSeeds.TestSeedVariable}={TestSeed} added seed {DifferentialSeeds.Derive(TestSeed)}");
        }

        int width = Paths.Select(static p => p.Name.Length).Append("oracle".Length).Max();
        var failingSeeds = new SortedSet<int>();
        bool multiShape = _executedShapes.Count > 1;
        foreach (int seed in Seeds)
        {
            var cells = _mismatches.Where(m => m.Seed == seed && !m.IsKnown).ToList();
            var failures = _failures.Where(f => f.Seed == seed && f.Known is null).ToList();
            var propFailures = _propertyFailures.Where(p => p.Seed == seed).ToList();
            if (cells.Count == 0 && failures.Count == 0 && propFailures.Count == 0)
            {
                continue;
            }

            failingSeeds.Add(seed);
            foreach (var failure in failures)
            {
                var ex = failure.Exception;
                string shapeTag = multiShape && failure.Shape is not null ? $" [{failure.Shape}]" : "";
                text.AppendLine($"  seed {SeedLabel(seed)}{shapeTag}: {Name(failure.Path)} threw {ex.GetType().FullName}: {ex.Message}");
            }

            foreach (var propFailure in propFailures)
            {
                string shapeTag = multiShape && propFailure.Shape is not null ? $" [{propFailure.Shape}]" : "";
                text.AppendLine($"  seed {SeedLabel(seed)}{shapeTag}: {Name(propFailure.Path)} failed property check: {propFailure.Message}");
            }

            if (cells.Count == 0)
            {
                continue;
            }

            text.AppendLine($"  seed {SeedLabel(seed)}, {cells.Count} mismatching cell{(cells.Count == 1 ? "" : "s")}; first:");
            foreach (var cell in cells.Take(CellsShownPerSeed))
            {
                string shapeSuffix = multiShape && cell.Shape is not null ? $" ({cell.Shape})" : "";
                text.AppendLine($"    {cell.Buffer}[{cell.Index}] thread {cell.Row}, column {cell.Column} \"{cell.ColumnName}\"{shapeSuffix}");
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
                $"remove it from {nameof(KnownDivergences)}.yaml (or narrow its paths) " +
                $"and close #{stale.Entry.Issue} if it is fixed"
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

    private static string Name(DiffPath path) => DiffPaths.Format(path);

    private static string SeedLabel(int seed) => seed == 0 ? "0 (boundary tables)" : seed.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static IEnumerable<string> DifferingPaths(IEnumerable<CellMismatch> cells) =>
        cells
            .SelectMany(static m => m.Paths)
            .Where(static p => p.Differs && p.Known is null)
            .Select(static p => Name(p.Path))
            .Distinct();
}
