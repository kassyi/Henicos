using System.Globalization;
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
        var kernelMethod = FindFirstMethod(root)
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
            AddDeadStoreStripCandidate(kernelMethod, removableIndices, TryAddCandidate);
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
        string casesUnit = BuildSpeculativeCasesCompilationUnit(candidates, fuzzSeed);

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

    private static MethodDeclarationSyntax? FindFirstMethod(CompilationUnitSyntax root)
    {
        var kernels = FuzzSyntaxHelpers.GetKernelMethods(root);
        if (kernels.Count > 0)
        {
            return kernels[0];
        }

        foreach (var node in root.DescendantNodes())
        {
            if (node is MethodDeclarationSyntax method)
            {
                return method;
            }
        }

        return null;
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
        string methodSource = FuzzSyntaxHelpers.RenderFullNode(renamedMethod);

        string bodyKey = renamedMethod.Body is not null ? FuzzSyntaxHelpers.RenderFullNode(renamedMethod.Body) : methodSource;
        if (!seenSources.Add(bodyKey))
        {
            return;
        }

        string caseHeader = string.Create(CultureInfo.InvariantCulture, $"// fuzz-seed: {FuzzRandom.FormatSeed(fuzzSeed)}");
        string caseSource = FuzzCaseEmitter.RenderDifferentialCaseProperty(
            "FuzzBatchKernels",
            candName,
            inputSeed,
            caseHeader);
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
            var mergedIndices = new int[combinedRemove.Count];
            combinedRemove.CopyTo(mergedIndices);
            var mergedMethod = FuzzSyntaxHelpers.RemoveStatementsFromMethodNode(kernelMethod, combinedRemove);
            tryAdd("IntersectionMerge", mergedIndices, mergedMethod);
        }
    }

    private static void AddDeadStoreStripCandidate(
        MethodDeclarationSyntax kernelMethod,
        IReadOnlyList<int> removableIndices,
        Action<string, IReadOnlyList<int>, MethodDeclarationSyntax> tryAdd)
    {
        if (kernelMethod.Body is null)
        {
            return;
        }

        var statements = kernelMethod.Body.Statements;
        var removableSet = new HashSet<int>(removableIndices);
        var liveVars = new HashSet<string>(StringComparer.Ordinal) { "checksum", "hash", "gid", "inA", "inB", "inC" };
        var deadIndices = new List<int>();

        for (int i = statements.Count - 1; i >= 0; i--)
        {
            var stmt = statements[i];
            if (removableSet.Contains(i))
            {
                if (stmt is LocalDeclarationStatementSyntax { Declaration.Variables.Count: 1 } localDecl)
                {
                    var v = localDecl.Declaration.Variables[0];
                    if (!liveVars.Contains(v.Identifier.ValueText) &&
                        (v.Initializer is null || !HasSideEffect(v.Initializer.Value)))
                    {
                        deadIndices.Add(i);
                        continue;
                    }
                }
                else if (stmt is ExpressionStatementSyntax
                {
                    Expression: AssignmentExpressionSyntax
                    {
                        RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                        Left: IdentifierNameSyntax leftId,
                    } simpleAssign,
                })
                {
                    if (!liveVars.Contains(leftId.Identifier.ValueText) && !HasSideEffect(simpleAssign.Right))
                    {
                        deadIndices.Add(i);
                        continue;
                    }

                    liveVars.Remove(leftId.Identifier.ValueText);
                    foreach (var node in simpleAssign.Right.DescendantNodesAndSelf())
                    {
                        if (node is IdentifierNameSyntax id)
                        {
                            liveVars.Add(id.Identifier.ValueText);
                        }
                    }

                    continue;
                }
            }

            foreach (var node in stmt.DescendantNodes())
            {
                if (node is IdentifierNameSyntax id)
                {
                    liveVars.Add(id.Identifier.ValueText);
                }
            }
        }

        if (deadIndices.Count > 0 && deadIndices.Count < removableIndices.Count)
        {
            deadIndices.Sort();
            var mutated = FuzzSyntaxHelpers.RemoveStatementsFromMethodNode(kernelMethod, new HashSet<int>(deadIndices));
            tryAdd("StripDeadStores", deadIndices, mutated);
        }
    }

    private static bool HasSideEffect(ExpressionSyntax expr)
    {
        foreach (var node in expr.DescendantNodesAndSelf())
        {
            if (node is AssignmentExpressionSyntax or InvocationExpressionSyntax ||
                (node is PrefixUnaryExpressionSyntax pre &&
                 (pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression))) ||
                (node is PostfixUnaryExpressionSyntax post &&
                 (post.IsKind(SyntaxKind.PostIncrementExpression) || post.IsKind(SyntaxKind.PostDecrementExpression))))
            {
                return true;
            }
        }

        return false;
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
        var backHalf = new int[n - mid];
        for (int i = mid; i < n; i++)
        {
            backHalf[i - mid] = removableIndices[i];
        }

        var frontHalf = new int[mid];
        for (int i = 0; i < mid; i++)
        {
            frontHalf[i] = removableIndices[i];
        }

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

            ExpressionSyntax? binOrCond = null;
            foreach (var node in stmt.DescendantNodes())
            {
                if (node is ConditionalExpressionSyntax or BinaryExpressionSyntax)
                {
                    binOrCond = (ExpressionSyntax)node;
                    break;
                }
            }

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
        FuzzCaseEmitter.AppendKernelsFileHeader(writer, "Kassyi.GSharp.Tests.Shared.Differential");

        var existingEnums = new List<EnumDeclarationSyntax>();
        var existingStructs = new List<StructDeclarationSyntax>();
        foreach (var node in originalRoot.DescendantNodes())
        {
            if (node is EnumDeclarationSyntax enumDecl)
            {
                existingEnums.Add(enumDecl);
            }
            else if (node is StructDeclarationSyntax structDecl)
            {
                existingStructs.Add(structDecl);
            }
        }

        if (existingEnums.Count > 0)
        {
            foreach (var enumDecl in existingEnums)
            {
                writer.AppendLine(FuzzSyntaxHelpers.RenderFullNode(enumDecl).AsSpan().Trim().ToString());
                writer.AppendLine();
            }
        }
        else
        {
            ScalarFuzzGenerator.AppendEnumDeclarations(writer);
            writer.AppendLine();
        }

        if (existingStructs.Count > 0)
        {
            foreach (var structDecl in existingStructs)
            {
                writer.AppendLine(FuzzSyntaxHelpers.RenderFullNode(structDecl).AsSpan().Trim().ToString());
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
                writer.AppendLine(FuzzSyntaxHelpers.RenderFullNode(helper).AsSpan().TrimEnd().ToString());
                writer.AppendLine();
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (i > 0)
                {
                    writer.AppendLine();
                }

                writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"// Speculative candidate #{candidates[i].Index:D2}: {candidates[i].CandidateDescription}"));
                writer.AppendLine(candidates[i].CandidateKernelMethodSource.AsSpan().TrimEnd().ToString());
            }
        }

        return writer.ToString();
    }

    private static string BuildSpeculativeCasesCompilationUnit(
        IReadOnlyList<SpeculativeBatchCandidate> candidates,
        ulong fuzzSeed)
    {
        var entries = new List<(string KernelName, ulong KernelSeed, string CasePropertySource)>(candidates.Count);
        foreach (var c in candidates)
        {
            entries.Add((c.CandidateKernelName, fuzzSeed, c.CandidateCasePropertySource));
        }

        return FuzzCaseEmitter.BuildCasesCompilationUnit(
            "Kassyi.GSharp.Tests.Shared.Differential",
            "FuzzBatchCases",
            fuzzSeed,
            entries,
            emitProgramMain: true);
    }
}
