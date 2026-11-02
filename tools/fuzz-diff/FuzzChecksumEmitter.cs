using System.Globalization;
using System.Text;

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

    /// <summary>Appends the standalone JSON-emitting batch runner <c>Main</c> method to <paramref name="sb"/>.</summary>
    public static void AppendBatchRunnerMain(StringBuilder sb)
    {
        ArgumentNullException.ThrowIfNull(sb);
        using var writer = new SourceWriter();
        AppendBatchRunnerMain(writer);
        sb.Append(writer.ToString());
    }

    /// <summary>Appends the standalone JSON-emitting batch runner <c>Main</c> method to <paramref name="writer"/>.</summary>
    public static void AppendBatchRunnerMain(SourceWriter writer)
    {
        using (writer.Scope("public static int Main(string[] args)"))
        {
            writer.AppendLine("bool managedOnly = args.Any(static a => string.Equals(a, \"--managed-only\", StringComparison.OrdinalIgnoreCase));");
            writer.AppendLine("int? explicitSeed = null;");
            using (writer.Scope("for (int i = 0; i < args.Length - 1; i++)"))
            {
                writer.AppendLine("if (string.Equals(args[i], \"--input-seed\", StringComparison.OrdinalIgnoreCase) &&");
                using (writer.Scope("int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))"))
                {
                    writer.AppendLine("explicitSeed = parsed;");
                }
            }

            writer.AppendLine();
            writer.AppendLine("IReadOnlyList<DiffPathRunner> activePaths = DiffPaths.Available;");
            using (writer.Scope("if (managedOnly)"))
            {
                writer.AppendLine("activePaths = activePaths.Where(static p => p.Path == DiffPath.Managed).ToArray();");
            }

            writer.AppendLine();
            writer.AppendLine("IReadOnlyList<int>? seeds = explicitSeed is { } s ? [s] : null;");
            writer.AppendLine("var cases = All.Select(static entry => entry.Case).ToArray();");
            writer.AppendLine("IReadOnlyList<DifferentialReport> reports;");
            using (writer.Scope("try"))
            {
                writer.AppendLine("reports = DifferentialRunner.EvaluateBatch(cases, seeds, activePaths, KnownDivergences.All, checkStale: false);");
            }

            using (writer.Scope("catch (Exception ex)"))
            {
                using (writer.Scope("Console.WriteLine(JsonSerializer.Serialize(new", "}));"))
                {
                    writer.AppendLine("status = \"harness_error\",");
                    writer.AppendLine("batchFuzzSeed = string.Create(CultureInfo.InvariantCulture, $\"0x{BatchFuzzSeed:X16}\"),");
                    writer.AppendLine("exceptionType = ex.GetType().FullName,");
                    writer.AppendLine("message = ex.Message,");
                }

                writer.AppendLine("return 2;");
            }

            writer.AppendLine();
            writer.AppendLine("int failureCount = 0;");
            using (writer.Scope("for (int i = 0; i < reports.Count; i++)"))
            {
                writer.AppendLine("var (kernelName, kernelSeed, _) = All[i];");
                writer.AppendLine("var report = reports[i];");
                using (writer.Scope("if (!report.HasFailures)"))
                {
                    writer.AppendLine("continue;");
                }

                writer.AppendLine();
                writer.AppendLine("failureCount++;");
                writer.AppendLine("var firstMismatch = report.UnexpectedMismatches.FirstOrDefault();");
                writer.AppendLine("var firstPathFailure = report.Failures.FirstOrDefault();");
                writer.AppendLine("var firstPropFailure = report.PropertyFailures.FirstOrDefault();");
                writer.AppendLine("int failInputSeed = firstMismatch?.Seed ?? firstPathFailure?.Seed ?? firstPropFailure?.Seed ?? 0;");
                writer.AppendLine("string failureKind = firstMismatch is not null");
                writer.AppendLine("? \"CellMismatch\"");
                writer.AppendLine(": firstPathFailure is not null");
                writer.AppendLine("? $\"RuntimeException:{firstPathFailure.Exception.GetType().Name}\"");
                writer.AppendLine(": \"PropertyFailure\";");
                writer.AppendLine("string[] failingPaths = firstMismatch is not null");
                writer.AppendLine("? firstMismatch.Paths.Where(static p => p.Differs).Select(static p => DiffPaths.Format(p.Path)).ToArray()");
                writer.AppendLine(": firstPathFailure is not null");
                writer.AppendLine("? [DiffPaths.Format(firstPathFailure.Path)]");
                writer.AppendLine(": firstPropFailure is not null");
                writer.AppendLine("? [DiffPaths.Format(firstPropFailure.Path)]");
                writer.AppendLine(": [];");
                writer.AppendLine();
                using (writer.Scope("Console.WriteLine(JsonSerializer.Serialize(new", "}));"))
                {
                    writer.AppendLine("status = \"failed\",");
                    writer.AppendLine("kernel = kernelName,");
                    writer.AppendLine("batchFuzzSeed = string.Create(CultureInfo.InvariantCulture, $\"0x{BatchFuzzSeed:X16}\"),");
                    writer.AppendLine("kernelSeed = string.Create(CultureInfo.InvariantCulture, $\"0x{kernelSeed:X16}\"),");
                    writer.AppendLine("inputSeed = failInputSeed,");
                    writer.AppendLine("failureKind,");
                    writer.AppendLine("failingPaths,");
                    writer.AppendLine("expected = firstMismatch?.Expected,");
                    writer.AppendLine("actual = firstMismatch?.Paths.FirstOrDefault(static p => p.Differs)?.Value ?? firstPathFailure?.Exception.Message ?? firstPropFailure?.Message,");
                    writer.AppendLine("reproCommand = string.Create(CultureInfo.InvariantCulture, $\"--fuzz-seed 0x{BatchFuzzSeed:X16} --input-seed {failInputSeed}\"),");
                    writer.AppendLine("report = report.ToString(),");
                }
            }

            writer.AppendLine();
            using (writer.Scope("Console.WriteLine(JsonSerializer.Serialize(new", "}));"))
            {
                writer.AppendLine("status = failureCount == 0 ? \"ok\" : \"batch_failed\",");
                writer.AppendLine("batchFuzzSeed = string.Create(CultureInfo.InvariantCulture, $\"0x{BatchFuzzSeed:X16}\"),");
                writer.AppendLine("kernelsEvaluated = reports.Count,");
                writer.AppendLine("kernelsFailed = failureCount,");
                writer.AppendLine("paths = activePaths.Select(static p => p.Name).ToArray(),");
            }

            writer.AppendLine("return failureCount == 0 ? 0 : 1;");
        }
    }
}
