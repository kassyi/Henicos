using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Emits 64-bit FNV-1a checksum folding statements over scalar locals, composite struct fields/properties,
/// <see cref="Nullable{T}"/> values, and fixed-size <see cref="Span{T}"/> buffers.
/// </summary>
public static class FuzzChecksumEmitter
{
    /// <summary>The 64-bit FNV-1a offset basis.</summary>
    public const ulong FnvOffsetBasis = 14695981039346656037UL;

    /// <summary>The 64-bit FNV-1a prime multiplier.</summary>
    public const ulong FnvPrime = 1099511628211UL;

    private const string BatchRunnerMainBlock =
        """
        public static int Main(string[] args)
        {
        bool managedOnly = args.Any(static a => string.Equals(a, "--managed-only", StringComparison.OrdinalIgnoreCase));
        int? explicitSeed = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
        if (string.Equals(args[i], "--input-seed", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
        explicitSeed = parsed;
        }
        }

        IReadOnlyList<DiffPathRunner> activePaths = DiffPaths.Available;
        if (managedOnly)
        {
        activePaths = activePaths.Where(static p => p.Path == DiffPath.Managed).ToArray();
        }

        IReadOnlyList<int>? seeds = explicitSeed is { } s ? [s] : null;
        var cases = All.Select(static entry => entry.Case).ToArray();
        IReadOnlyList<DifferentialReport> reports;
        try
        {
        reports = DifferentialRunner.EvaluateBatch(cases, seeds, activePaths, KnownDivergences.All, checkStale: false);
        }
        catch (Exception ex)
        {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
        status = "harness_error",
        batchFuzzSeed = string.Create(CultureInfo.InvariantCulture, $"0x{BatchFuzzSeed:X16}"),
        exceptionType = ex.GetType().FullName,
        message = ex.Message,
        }));
        return 2;
        }

        int failureCount = 0;
        for (int i = 0; i < reports.Count; i++)
        {
        var (kernelName, kernelSeed, _) = All[i];
        var report = reports[i];
        if (!report.HasFailures)
        {
        continue;
        }

        failureCount++;
        var firstMismatch = report.UnexpectedMismatches.FirstOrDefault();
        var firstPathFailure = report.Failures.FirstOrDefault();
        var firstPropFailure = report.PropertyFailures.FirstOrDefault();
        int failInputSeed = firstMismatch?.Seed ?? firstPathFailure?.Seed ?? firstPropFailure?.Seed ?? 0;
        string failureKind = firstMismatch is not null
        ? "CellMismatch"
        : firstPathFailure is not null
        ? $"RuntimeException:{firstPathFailure.Exception.GetType().Name}"
        : "PropertyFailure";
        string[] failingPaths = firstMismatch is not null
        ? firstMismatch.Paths.Where(static p => p.Differs).Select(static p => DiffPaths.Format(p.Path)).ToArray()
        : firstPathFailure is not null
        ? [DiffPaths.Format(firstPathFailure.Path)]
        : firstPropFailure is not null
        ? [DiffPaths.Format(firstPropFailure.Path)]
        : [];

        Console.WriteLine(JsonSerializer.Serialize(new
        {
        status = "failed",
        kernel = kernelName,
        batchFuzzSeed = string.Create(CultureInfo.InvariantCulture, $"0x{BatchFuzzSeed:X16}"),
        kernelSeed = string.Create(CultureInfo.InvariantCulture, $"0x{kernelSeed:X16}"),
        inputSeed = failInputSeed,
        column = firstMismatch?.ColumnName ?? (firstPathFailure is not null ? KnownDivergence.ExceptionColumn : "hash"),
        failureKind,
        failingPaths,
        expected = firstMismatch?.Expected,
        actual = firstMismatch?.Paths.FirstOrDefault(static p => p.Differs)?.Value ?? firstPathFailure?.Exception.Message ?? firstPropFailure?.Message,
        reproCommand = string.Create(CultureInfo.InvariantCulture, $"--fuzz-seed 0x{BatchFuzzSeed:X16} --input-seed {failInputSeed}"),
        report = report.ToString(),
        }));
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
        status = failureCount == 0 ? "ok" : "batch_failed",
        batchFuzzSeed = string.Create(CultureInfo.InvariantCulture, $"0x{BatchFuzzSeed:X16}"),
        kernelsEvaluated = reports.Count,
        kernelsFailed = failureCount,
        paths = activePaths.Select(static p => p.Name).ToArray(),
        }));
        return failureCount == 0 ? 0 : 1;
        }
        """;

    /// <summary>Emits a subset of checksum folds at intermediate points in the kernel body.</summary>
    public static void EmitChecksumFoldSubset(KernelGenerationState state)
    {
        var rng = state.Rng;
        int count = Math.Min(3, state.Locals.Count);
        for (int i = 0; i < count; i++)
        {
            var local = rng.Choose(state.Locals);
            EmitChecksumFoldSingle(state, local);
        }
    }

    /// <summary>Emits the final checksum fold of all in-scope scalar locals, struct members, <see cref="Nullable{T}"/> slots, and <see cref="Span{T}"/> elements.</summary>
    public static void EmitChecksumFoldAll(KernelGenerationState state)
    {
        foreach (var local in state.Locals)
        {
            EmitChecksumFoldSingle(state, local);
        }

        if (state.StructLocals.Count > 0)
        {
            EmitChecksumFoldExpression(state, "((ulong)(long)sWrap.Shifted)");
            EmitChecksumFoldExpression(state, "((ulong)(long)sL1.CompA)");
            EmitChecksumFoldExpression(state, "((ulong)sL2.Summary)");
            EmitChecksumFoldExpression(state, "((ulong)sL3.Total)");
            EmitChecksumFoldExpression(state, "((ulong)(long)sPack1.PackedSum)");
            EmitChecksumFoldExpression(state, "((ulong)sPack4.PackedWide)");
            EmitChecksumFoldExpression(state, "((ulong)(long)(sNullBag.OptInt0 ?? -101))");
            EmitChecksumFoldExpression(state, "((ulong)(long)(sNullBag.OptInt1 ?? -202))");
            EmitChecksumFoldExpression(state, "((ulong)(sNullBag.OptLong0 ?? -303L))");
        }

        if (!state.IssueFilter.AvoidNullableLocalVariables)
        {
            foreach (var slot in state.NullableSlots)
            {
                if (slot.IsLocalVariable)
                {
                    EmitChecksumFoldExpression(state, $"((ulong)(long)({slot.AccessExpression} ?? -404))");
                }
            }
        }

        if (state.SpanLocals.Count > 0)
        {
            var sp = state.SpanLocals[0];
            for (int idx = 0; idx < sp.Length; idx++)
            {
                EmitChecksumFoldExpression(state, string.Create(CultureInfo.InvariantCulture, $"((ulong)(long){sp.Name}[{idx}])"));
            }
        }
    }

    /// <summary>Emits a single FNV-1a 64-bit checksum fold for <paramref name="local"/>.</summary>
    public static void EmitChecksumFoldSingle(KernelGenerationState state, ScalarLocalVariable local)
    {
        string bitsExpr = local.Type.Kind switch
        {
            FuzzScalarKind.Boolean => $"({local.Name} ? 1UL : 0UL)",
            _ => $"((ulong){local.Name})",
        };
        EmitChecksumFoldExpression(state, bitsExpr);
    }

    /// <summary>Emits a single FNV-1a 64-bit checksum fold for an arbitrary <see langword="ulong"/> expression.</summary>
    public static void EmitChecksumFoldExpression(KernelGenerationState state, string ulongExpr)
    {
        ulong siteId = ++state.ChecksumSiteId;
        state.Statements.Add(
            string.Create(
                CultureInfo.InvariantCulture,
                $"hash = (hash ^ ({ulongExpr} + {siteId}UL)) * {FnvPrime}UL;"));
    }

    /// <summary>Appends the standalone JSON-emitting batch runner <c>Main</c> method to <paramref name="writer"/>.</summary>
    public static void AppendBatchRunnerMain(SourceWriter writer) =>
        writer.AppendLine(BatchRunnerMainBlock);
}
