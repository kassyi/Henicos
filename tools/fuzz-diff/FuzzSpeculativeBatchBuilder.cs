using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Represents a single speculative reduction candidate generated for batched CUDA reduction.
/// </summary>
public sealed record SpeculativeBatchCandidate(
    int Index,
    string CandidateKernelName,
    string CandidateDescription,
    string CandidateKernelMethodSource,
    string CandidateCasePropertySource,
    IReadOnlyList<int> RemovedStatementIndices,
    int StatementCount);

/// <summary>
/// Represents a batch of up to 16 speculative reduction candidates packed into a single <c>.cu.cs</c> and <c>FuzzBatchCases.cs</c>
/// so a single <c>nvcc</c> invocation compiles and evaluates all candidates at once.
/// </summary>
public sealed record SpeculativeCandidateBatch(
    string BaseKernelName,
    ulong FuzzSeed,
    int? InputSeed,
    string KernelsSource,
    string CasesSource,
    IReadOnlyList<SpeculativeBatchCandidate> Candidates);

/// <summary>
/// Builds 16-candidate speculative reduction batches (<c>Candidate_00</c>..<c>Candidate_15</c>)
/// and selects the winning reduced candidate from batch execution results.
/// </summary>
public static class FuzzSpeculativeBatchBuilder
{
    /// <summary>
    /// Generates up to <paramref name="maxCandidates"/> (default 16) independent speculative reduction
    /// candidates (<c>Candidate_00</c>..<c>Candidate_15</c>) packed into a single <c>.cu.cs</c> and <c>FuzzBatchCases.cs</c>
    /// so a single <c>nvcc</c> invocation compiles and evaluates all 16 candidates at once.
    /// </summary>
    public static SpeculativeCandidateBatch GenerateSpeculativeBatchCandidates(
        string kernelSource,
        ulong fuzzSeed = 1UL,
        int? inputSeed = null,
        int maxCandidates = 16,
        IReadOnlyList<IReadOnlyList<int>>? priorSuccessfulRemovals = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kernelSource);
        maxCandidates = Math.Clamp(maxCandidates, 1, 16);

        string hoistedSource = FuzzReducer.HoistLocalsToDefault(kernelSource);
        var root = FuzzSyntaxHelpers.ParseRoot(hoistedSource);
        var kernelMethod = FuzzSyntaxHelpers.GetKernelMethods(root).FirstOrDefault()
            ?? root.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException("No kernel method found in source.");

        if (kernelMethod.Body is null)
        {
            throw new InvalidOperationException("Kernel method has no body.");
        }

        string baseKernelName = kernelMethod.Identifier.ValueText;
        var removableIndices = FuzzSyntaxHelpers.GetRemovableStatementIndices(kernelMethod.Body.Statements);
        var candidates = new List<SpeculativeBatchCandidate>(maxCandidates);
        var seenSources = new HashSet<string>(StringComparer.Ordinal);

        void TryAddCandidate(string description, IReadOnlyList<int> removedIndices, MethodDeclarationSyntax mutatedMethod) =>
            TryAddSpeculativeCandidate(candidates, seenSources, maxCandidates, fuzzSeed, inputSeed, description, removedIndices, mutatedMethod);

        AddIntersectionMergeCandidate(kernelMethod, removableIndices, priorSuccessfulRemovals, TryAddCandidate);
        if (removableIndices.Count > 0)
        {
            AddQuarterChunkCandidates(kernelMethod, removableIndices, TryAddCandidate);
            AddHalfChunkCandidates(kernelMethod, removableIndices, TryAddCandidate);
            AddSingleStatementCandidates(kernelMethod, removableIndices, candidates, maxCandidates, TryAddCandidate);
            AddExpressionSimplificationCandidates(kernelMethod, removableIndices, candidates, maxCandidates, TryAddCandidate);
        }

        if (candidates.Count == 0)
        {
            TryAddCandidate("Identity", [], kernelMethod);
        }

        string kernelsUnit = BuildSpeculativeKernelsCompilationUnit(root, candidates);
        string casesUnit = BuildSpeculativeCasesCompilationUnit(candidates, fuzzSeed, inputSeed);

        return new SpeculativeCandidateBatch(
            baseKernelName,
            fuzzSeed,
            inputSeed,
            kernelsUnit,
            casesUnit,
            candidates);
    }

    /// <summary>
    /// Selects the winning candidate from a speculative 16-candidate batch given the predicate <paramref name="didCandidateFail"/>,
    /// picking the smallest candidate that still reproduces the failure.
    /// </summary>
    public static SpeculativeBatchCandidate? SelectBestSpeculativeCandidate(
        SpeculativeCandidateBatch batch,
        Func<SpeculativeBatchCandidate, bool> didCandidateFail)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(didCandidateFail);

        SpeculativeBatchCandidate? best = null;
        foreach (var candidate in batch.Candidates)
        {
            if (!didCandidateFail(candidate))
            {
                continue;
            }

            if (best is null ||
                candidate.StatementCount < best.StatementCount ||
                (candidate.StatementCount == best.StatementCount &&
                 candidate.CandidateKernelMethodSource.Length < best.CandidateKernelMethodSource.Length))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static void TryAddSpeculativeCandidate(
        List<SpeculativeBatchCandidate> candidates,
        HashSet<string> seenSources,
        int maxCandidates,
        ulong fuzzSeed,
        int? inputSeed,
        string description,
        IReadOnlyList<int> removedIndices,
        MethodDeclarationSyntax mutatedMethod)
    {
        if (candidates.Count >= maxCandidates)
        {
            return;
        }

        int idx = candidates.Count;
        string candName = string.Create(CultureInfo.InvariantCulture, $"Candidate_{idx:D2}");
        var renamedMethod = mutatedMethod.WithIdentifier(SyntaxFactory.Identifier(candName));
        string methodSource = renamedMethod.ToFullString();

        string bodyKey = renamedMethod.Body?.ToFullString() ?? methodSource;
        if (!seenSources.Add(bodyKey))
        {
            return;
        }

        string caseSource = RenderCandidateCaseProperty(candName, fuzzSeed, inputSeed);
        int stmtCount = FuzzSyntaxHelpers.CountMethodExecutableStatements(renamedMethod);
        candidates.Add(new SpeculativeBatchCandidate(
            idx,
            candName,
            description,
            methodSource,
            caseSource,
            removedIndices,
            stmtCount));
    }

    private static void AddIntersectionMergeCandidate(
        MethodDeclarationSyntax kernelMethod,
        IReadOnlyList<int> removableIndices,
        IReadOnlyList<IReadOnlyList<int>>? priorSuccessfulRemovals,
        Action<string, IReadOnlyList<int>, MethodDeclarationSyntax> tryAdd)
    {
        if (priorSuccessfulRemovals is not { Count: >= 2 })
        {
            return;
        }

        var combinedRemove = new SortedSet<int>();
        foreach (var list in priorSuccessfulRemovals)
        {
            foreach (int r in list)
            {
                combinedRemove.Add(r);
            }
        }

        if (combinedRemove.Count > 0 && combinedRemove.Count < removableIndices.Count)
        {
            var mergedMethod = FuzzSyntaxHelpers.RemoveStatementsFromMethodNode(kernelMethod, combinedRemove);
            tryAdd("IntersectionMerge", combinedRemove.ToArray(), mergedMethod);
        }
    }

    private static void AddQuarterChunkCandidates(
        MethodDeclarationSyntax kernelMethod,
        IReadOnlyList<int> removableIndices,
        Action<string, IReadOnlyList<int>, MethodDeclarationSyntax> tryAdd)
    {
        int n = removableIndices.Count;
        int chunkCount = Math.Min(4, n);
        int chunkSize = Math.Max(1, n / chunkCount);
        for (int c = chunkCount - 1; c >= 0; c--)
        {
            int start = c * chunkSize;
            int end = (c == chunkCount - 1) ? n : Math.Min(n, (c + 1) * chunkSize);
            if (start >= end)
            {
                continue;
            }

            var rem = new List<int>(end - start);
            for (int i = start; i < end; i++)
            {
                rem.Add(removableIndices[i]);
            }

            var mutated = FuzzSyntaxHelpers.RemoveStatementsFromMethodNode(kernelMethod, new HashSet<int>(rem));
            tryAdd($"Chunk4_{c}", rem, mutated);
        }
    }

    private static void AddHalfChunkCandidates(
        MethodDeclarationSyntax kernelMethod,
        IReadOnlyList<int> removableIndices,
        Action<string, IReadOnlyList<int>, MethodDeclarationSyntax> tryAdd)
    {
        int n = removableIndices.Count;
        if (n < 2)
        {
            return;
        }

        int mid = n / 2;
        var backHalf = removableIndices.Skip(mid).ToArray();
        var frontHalf = removableIndices.Take(mid).ToArray();
        tryAdd("Half_Back", backHalf, FuzzSyntaxHelpers.RemoveStatementsFromMethodNode(kernelMethod, new HashSet<int>(backHalf)));
        tryAdd("Half_Front", frontHalf, FuzzSyntaxHelpers.RemoveStatementsFromMethodNode(kernelMethod, new HashSet<int>(frontHalf)));
    }

    private static void AddSingleStatementCandidates(
        MethodDeclarationSyntax kernelMethod,
        IReadOnlyList<int> removableIndices,
        IReadOnlyList<SpeculativeBatchCandidate> candidates,
        int maxCandidates,
        Action<string, IReadOnlyList<int>, MethodDeclarationSyntax> tryAdd)
    {
        for (int i = removableIndices.Count - 1; i >= 0 && candidates.Count < maxCandidates; i--)
        {
            int stmtIdx = removableIndices[i];
            int[] singleRem = [stmtIdx];
            var mutated = FuzzSyntaxHelpers.RemoveStatementsFromMethodNode(kernelMethod, new HashSet<int>(singleRem));
            tryAdd($"RemoveStmt_{stmtIdx}", singleRem, mutated);
        }
    }

    private static void AddExpressionSimplificationCandidates(
        MethodDeclarationSyntax kernelMethod,
        IReadOnlyList<int> removableIndices,
        IReadOnlyList<SpeculativeBatchCandidate> candidates,
        int maxCandidates,
        Action<string, IReadOnlyList<int>, MethodDeclarationSyntax> tryAdd)
    {
        for (int i = 0; i < removableIndices.Count && candidates.Count < maxCandidates; i++)
        {
            int stmtIdx = removableIndices[i];
            var stmt = kernelMethod.Body!.Statements[stmtIdx];
            if (FuzzSyntaxHelpers.IsChecksumFoldStatement(stmt))
            {
                continue;
            }

            var binOrCond = stmt.DescendantNodes()
                .OfType<ExpressionSyntax>()
                .FirstOrDefault(static e => e is ConditionalExpressionSyntax or BinaryExpressionSyntax);

            if (binOrCond is ConditionalExpressionSyntax cond)
            {
                var newStmtTrue = stmt.ReplaceNode(cond, SyntaxFactory.ParenthesizedExpression(cond.WhenTrue.WithoutTrivia()));
                tryAdd($"CondTrue_{stmtIdx}", [], FuzzSyntaxHelpers.ReplaceSingleStatement(kernelMethod, stmtIdx, newStmtTrue));
                var newStmtFalse = stmt.ReplaceNode(cond, SyntaxFactory.ParenthesizedExpression(cond.WhenFalse.WithoutTrivia()));
                tryAdd($"CondFalse_{stmtIdx}", [], FuzzSyntaxHelpers.ReplaceSingleStatement(kernelMethod, stmtIdx, newStmtFalse));
            }
            else if (binOrCond is BinaryExpressionSyntax bin && !FuzzSyntaxHelpers.IsBooleanComparison(bin))
            {
                var newStmtLeft = stmt.ReplaceNode(bin, SyntaxFactory.ParenthesizedExpression(bin.Left.WithoutTrivia()));
                tryAdd($"BinLeft_{stmtIdx}", [], FuzzSyntaxHelpers.ReplaceSingleStatement(kernelMethod, stmtIdx, newStmtLeft));
            }
        }
    }

    private static string BuildSpeculativeKernelsCompilationUnit(
        CompilationUnitSyntax originalRoot,
        IReadOnlyList<SpeculativeBatchCandidate> candidates)
    {
        using var writer = new SourceWriter();
        writer.AppendLine("#nullable enable");
        writer.AppendLine("#pragma warning disable CS0162, CS0219, CS1717, CS1718");
        writer.AppendLine("using System;");
        writer.AppendLine("using Kassyi.GSharp.Core;");
        writer.AppendLine();
        writer.AppendLine("namespace Kassyi.GSharp.Tests.Shared.Differential;");
        writer.AppendLine();

        var existingEnums = originalRoot.DescendantNodes().OfType<EnumDeclarationSyntax>().ToList();
        if (existingEnums.Count > 0)
        {
            foreach (var enumDecl in existingEnums)
            {
                writer.AppendLine(enumDecl.ToFullString().Trim());
                writer.AppendLine();
            }
        }
        else
        {
            ScalarFuzzGenerator.AppendEnumDeclarations(writer);
            writer.AppendLine();
        }

        var existingStructs = originalRoot.DescendantNodes().OfType<StructDeclarationSyntax>().ToList();
        if (existingStructs.Count > 0)
        {
            foreach (var structDecl in existingStructs)
            {
                writer.AppendLine(structDecl.ToFullString().Trim());
                writer.AppendLine();
            }
        }
        else
        {
            FuzzCompositeModel.AppendCompositeStructDeclarations(writer);
            writer.AppendLine();
        }

        using (writer.Scope("public static partial class FuzzBatchKernels"))
        {
            foreach (var helper in FuzzSyntaxHelpers.GetDeviceFunctionMethods(originalRoot))
            {
                writer.AppendLine(helper.ToFullString().TrimEnd());
                writer.AppendLine();
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (i > 0)
                {
                    writer.AppendLine();
                }

                writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"// Speculative candidate #{candidates[i].Index:D2}: {candidates[i].CandidateDescription}"));
                writer.AppendLine(candidates[i].CandidateKernelMethodSource.TrimEnd());
            }
        }

        return writer.ToString();
    }

    private static string BuildSpeculativeCasesCompilationUnit(
        IReadOnlyList<SpeculativeBatchCandidate> candidates,
        ulong fuzzSeed,
        int? inputSeed)
    {
        using var writer = new SourceWriter();
        writer.AppendLine("// <auto-generated />");
        writer.AppendLine("#nullable enable");
        writer.AppendLine("using System;");
        writer.AppendLine("using System.Collections.Generic;");
        writer.AppendLine("using System.Globalization;");
        writer.AppendLine("using System.Linq;");
        writer.AppendLine("using System.Text.Json;");
        writer.AppendLine("using Kassyi.GSharp.Core;");
        writer.AppendLine();
        writer.AppendLine("namespace Kassyi.GSharp.Tests.Shared.Differential;");
        writer.AppendLine();

        using (writer.Scope("public static class FuzzBatchCases"))
        {
            writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"public const ulong BatchFuzzSeed = {fuzzSeed}UL;"));
            writer.AppendLine();

            foreach (var c in candidates)
            {
                writer.AppendLine(c.CandidateCasePropertySource);
                writer.AppendLine();
            }

            writer.AppendLine("public static IReadOnlyList<(string KernelName, ulong KernelSeed, DifferentialCase Case)> All { get; } =");
            writer.AppendLine("[");
            foreach (var c in candidates)
            {
                writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"(\"{c.CandidateKernelName}\", {fuzzSeed}UL, {c.CandidateKernelName}_Case),"));
            }

            writer.AppendLine("];");

            writer.AppendLine();
            FuzzChecksumEmitter.AppendBatchRunnerMain(writer);
        }

        return writer.ToString();
    }

    private static string RenderCandidateCaseProperty(string candidateKernelName, ulong fuzzSeed, int? inputSeed)
    {
        using var writer = new SourceWriter();
        writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"// fuzz-seed: {FuzzRandom.FormatSeed(fuzzSeed)}"));
        using (writer.Scope($"public static DifferentialCase {candidateKernelName}_Case {{ get; }} = new(\"FuzzBatchKernels.{candidateKernelName}\")", "};"))
        {
            writer.AppendLine("Shape = LaunchShapes.PartialWarp(),");
            if (inputSeed is { } explicitInputSeed)
            {
                writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Seeds = [{explicitInputSeed}],"));
            }

            using (writer.Scope("Inputs = static (rng, shape) =>", "},"))
            {
                writer.AppendLine("int rows = shape.Rows;");
                writer.AppendLine("var (inA, inB) = DiffValues.Int32Pairs(rng, rows);");
                writer.AppendLine("var inC = DiffValues.Values(rng, rows, DiffValues.ShiftCounts, DiffValues.NextInt32);");
                writer.AppendLine("return new DiffBuffers()");
                writer.AppendLine(".In(\"inA\", inA)");
                writer.AppendLine(".In(\"inB\", inB)");
                writer.AppendLine(".In(\"inC\", inC)");
                writer.AppendLine(".Out<ulong>(\"checksum\", rows, [\"hash\"], OutputPolicy.Exact);");
            }

            writer.AppendLine($"Launch = static (b, s) => FuzzBatchKernels.Launch{candidateKernelName}(");
            writer.AppendLine("b.Get<int>(\"inA\"),");
            writer.AppendLine("b.Get<int>(\"inB\"),");
            writer.AppendLine("b.Get<int>(\"inC\"),");
            writer.AppendLine("b.Get<ulong>(\"checksum\"),");
            writer.AppendLine("gridDimX: s.GridX,");
            writer.AppendLine("blockDimX: s.BlockX),");
            writer.AppendLine($"Oracle = static (b, _) => FuzzBatchKernels.{candidateKernelName}(");
            writer.AppendLine("b.Get<int>(\"inA\"),");
            writer.AppendLine("b.Get<int>(\"inB\"),");
            writer.AppendLine("b.Get<int>(\"inC\"),");
            writer.AppendLine("b.Get<ulong>(\"checksum\")),");
        }

        return writer.ToString().TrimEnd();
    }
}

