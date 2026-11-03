using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Result of running <see cref="FuzzReducer"/> on a failing kernel compilation unit or method.
/// </summary>
public sealed record FuzzReductionResult(
    string ReducedSource,
    int OriginalStatementCount,
    int ReducedStatementCount,
    int OriginalDeviceFunctionCount,
    int ReducedDeviceFunctionCount,
    int Iterations);

/// <summary>
/// Captures the original failure signature that every reduction step must preserve:
/// the normalized set of failing execution paths, the failure kind tag, and (for cell mismatches) the mismatching column name.
/// </summary>
public readonly record struct FuzzFailureSignature(
    string FailingPathsMask,
    string FailureKind,
    string Column = "hash")
{
    /// <summary>Creates a canonical <see cref="FuzzFailureSignature"/>.</summary>
    public static FuzzFailureSignature Create(
        string? failingPaths,
        string? failureKind,
        string? column = "hash")
    {
        string normPaths = FuzzDeduplicator.NormalizeFailingPathsMask(failingPaths);
        string normKind = string.IsNullOrWhiteSpace(failureKind) ? "CellMismatch" : failureKind.AsSpan().Trim().ToString();
        string normCol = string.IsNullOrWhiteSpace(column) ? "hash" : column.AsSpan().Trim().ToString();
        return new FuzzFailureSignature(normPaths, normKind, normCol);
    }

    /// <summary>
    /// Returns <see langword="true"/> only when <paramref name="candidate"/> has the exact same failing-path set,
    /// failure kind, and (when <see cref="FailureKind"/> is <c>"CellMismatch"</c>) column name.
    /// </summary>
    public bool Matches(FuzzFailureSignature candidate) =>
        Matches(candidate.FailingPathsMask, candidate.FailureKind, candidate.Column);

    /// <summary>
    /// Returns <see langword="true"/> only when the candidate failure has the exact same failing-path set,
    /// failure kind, and (when <see cref="FailureKind"/> is <c>"CellMismatch"</c>) column name.
    /// </summary>
    public bool Matches(string? candidateFailingPaths, string? candidateFailureKind, string? candidateColumn = "hash")
    {
        string candPaths = FuzzDeduplicator.NormalizeFailingPathsMask(candidateFailingPaths);
        if (!string.Equals(candPaths, FailingPathsMask, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string candKind = string.IsNullOrWhiteSpace(candidateFailureKind)
            ? "CellMismatch"
            : candidateFailureKind.AsSpan().Trim().ToString();
        if (!string.Equals(candKind, FailureKind, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(FailureKind, "CellMismatch", StringComparison.Ordinal))
        {
            string candCol = string.IsNullOrWhiteSpace(candidateColumn)
                ? "hash"
                : candidateColumn.AsSpan().Trim().ToString();
            if (!string.Equals(candCol, Column, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Roslyn syntax-tree-based reducer for failing differential fuzz kernels:
/// <list type="number">
/// <item><description><b>In-memory / single-candidate reduction</b>: Hoist all non-<see langword="ref"/> local variable declarations to <c>T v = default(T);</c> at the top of the method body, inline/prune helper <c>[GpuDeviceFunction]</c> methods, run binary-search coarse statement removal (<c>ddmin</c>), simplify expressions to a fixed point, and prune unused local declarations.</description></item>
/// <item><description><b>Batched CUDA reduction (16-candidate speculative batch reduction)</b>: Pack up to 16 independent reduced candidate kernels (<c>Candidate_00</c>..<c>Candidate_15</c>) into a single <c>.cu.cs</c> + <c>FuzzBatchCases.cs</c> so one <c>nvcc</c> build tests all 16 candidates simultaneously, plus intersection merging of disjoint successful chunk removals.</description></item>
/// </list>
/// </summary>
public static class FuzzReducer
{
    /// <summary>
    /// Reduces <paramref name="source"/> while preserving the exact <paramref name="expectedFailure"/> signature
    /// (failing-path set, failure kind, and mismatch column).
    /// </summary>
    public static FuzzReductionResult Reduce(
        string source,
        FuzzFailureSignature expectedFailure,
        Func<string, FuzzFailureSignature?> evaluateCandidate,
        bool avoidStructConversionDefault = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(evaluateCandidate);

        return Reduce(
            source,
            candidate => evaluateCandidate(candidate) is { } actual && expectedFailure.Matches(actual),
            avoidStructConversionDefault);
    }

    /// <summary>
    /// Reduces <paramref name="source"/> while <paramref name="isInteresting"/> returns <see langword="true"/>.
    /// </summary>
    public static FuzzReductionResult Reduce(
        string source,
        Func<string, bool> isInteresting,
        bool avoidStructConversionDefault = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(isInteresting);

        int origStatements = CountKernelExecutableStatements(source);
        int origDeviceFuncs = CountDeviceFunctionMethods(source);
        int iterations = 0;

        bool TestCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !FuzzSyntaxHelpers.HasValidSyntax(candidate))
            {
                return false;
            }

            iterations++;
            return isInteresting(candidate);
        }

        string current = source;

        // Step 1: Hoist all non-ref local variable declarations to `T v = default(T);` at the top of each method body.
        string hoisted = HoistLocalsToDefault(current, avoidStructConversionDefault);
        if (!string.Equals(hoisted, current, StringComparison.Ordinal) && TestCandidate(hoisted))
        {
            current = hoisted;
        }

        // Step 2: Inline and prune [GpuDeviceFunction] helper methods.
        current = FuzzInlinerAndSimplifier.InlineAndPruneDeviceFunctions(current, TestCandidate);

        // Re-hoist locals in case inlining introduced new local declarations.
        string rehoisted = HoistLocalsToDefault(current, avoidStructConversionDefault);
        if (!string.Equals(rehoisted, current, StringComparison.Ordinal) && TestCandidate(rehoisted))
        {
            current = rehoisted;
        }

        // Step 3 & 4: Fixed-point loop of binary-search coarse statement removal, expression simplification, and unused local pruning.
        for (int pass = 0; pass < 6; pass++)
        {
            string beforePass = current;

            current = CoarseRemoveStatements(current, TestCandidate);
            current = FuzzInlinerAndSimplifier.InlineAndPruneDeviceFunctions(current, TestCandidate);
            current = FuzzInlinerAndSimplifier.SimplifyExpressions(current, TestCandidate);
            current = FuzzInlinerAndSimplifier.PruneUnusedHoistedLocals(current, TestCandidate);
            current = FuzzInlinerAndSimplifier.CollapseSingleAssignmentLocals(current, TestCandidate);
            current = FuzzInlinerAndSimplifier.PruneUnusedHoistedLocals(current, TestCandidate);

            if (string.Equals(current, beforePass, StringComparison.Ordinal))
            {
                break;
            }
        }

        int finalStatements = CountKernelExecutableStatements(current);
        int finalDeviceFuncs = CountDeviceFunctionMethods(current);
        return new FuzzReductionResult(current, origStatements, finalStatements, origDeviceFuncs, finalDeviceFuncs, iterations);
    }

    /// <summary>
    /// Counts the number of executable statements inside the <c>[GpuKernel]</c> method(s) of <paramref name="source"/>,
    /// excluding hoisted <c>T v = default(T);</c> declarations so the metric reflects active kernel statements.
    /// </summary>
    public static int CountKernelExecutableStatements(string source)
    {
        var root = FuzzSyntaxHelpers.ParseRoot(source);
        var kernelMethods = FuzzSyntaxHelpers.GetKernelMethods(root);
        if (kernelMethods.Count == 0)
        {
            kernelMethods = CollectAllMethods(root);
        }

        int count = 0;
        foreach (var method in kernelMethods)
        {
            count += FuzzSyntaxHelpers.CountMethodExecutableStatements(method);
        }

        return count;
    }

    /// <summary>Counts the number of <c>[GpuDeviceFunction]</c> helper methods in <paramref name="source"/>.</summary>
    public static int CountDeviceFunctionMethods(string source)
    {
        var root = FuzzSyntaxHelpers.ParseRoot(source);
        return FuzzSyntaxHelpers.GetDeviceFunctionMethods(root).Count;
    }

    /// <summary>
    /// Hoists all non-<see langword="ref"/> local variable declarations inside methods to <c>T v = default(T);</c>
    /// right after the <c>gid</c> bounds guard (or at the top of the method body), converting original declaration sites
    /// <c>T v = expr;</c> into simple assignment statements <c>v = expr;</c> (Fuzzlyn <c>Reducer.CoarseSimplify</c>).
    /// While <c>#1103</c> is open (<paramref name="avoidStructConversionDefault"/> is <see langword="true"/>), structs
    /// that declare a conversion operator (or contain such a struct as a field) are not hoisted to <c>default(T)</c>.
    /// </summary>
    public static string HoistLocalsToDefault(string source, bool avoidStructConversionDefault = true)
    {
        var root = FuzzSyntaxHelpers.ParseRoot(source);
        var methods = CollectAllMethods(root);
        if (methods.Count == 0)
        {
            return source;
        }

        var updatedRoot = root.ReplaceNodes(methods, (_, currentMethod) => HoistMethodLocals(root, currentMethod, avoidStructConversionDefault));
        return FuzzSyntaxHelpers.RenderFullNode(updatedRoot);
    }

    /// <summary>
    /// Binary-search coarse statement remover (<c>CoarseStatementRemover</c> / <c>ddmin</c>).
    /// Preserves the <c>gid</c> bounds prologue, hoisted <c>T v = default(T);</c> declarations, <c>ulong hash = ...;</c> initialization,
    /// and final <c>checksum[gid] = hash;</c> write while deleting removable statements in binary chunks.
    /// </summary>
    public static string CoarseRemoveStatements(string source, Func<string, bool> isInteresting)
    {
        string current = source;
        var root = FuzzSyntaxHelpers.ParseRoot(current);
        var kernelMethods = FuzzSyntaxHelpers.GetKernelMethods(root);
        if (kernelMethods.Count == 0)
        {
            kernelMethods = CollectAllMethods(root);
        }

        for (int mIndex = 0; mIndex < kernelMethods.Count; mIndex++)
        {
            string methodName = kernelMethods[mIndex].Identifier.ValueText;
            current = CoarseRemoveStatementsInMethod(current, methodName, isInteresting);
        }

        return current;
    }

    /// <summary>Forwards to <see cref="FuzzInlinerAndSimplifier.InlineAndPruneDeviceFunctions"/>.</summary>
    public static string InlineAndPruneDeviceFunctions(string source, Func<string, bool> isInteresting) =>
        FuzzInlinerAndSimplifier.InlineAndPruneDeviceFunctions(source, isInteresting);

    /// <summary>Forwards to <see cref="FuzzInlinerAndSimplifier.SimplifyExpressions"/>.</summary>
    public static string SimplifyExpressions(string source, Func<string, bool> isInteresting) =>
        FuzzInlinerAndSimplifier.SimplifyExpressions(source, isInteresting);

    /// <summary>Forwards to <see cref="FuzzInlinerAndSimplifier.PruneUnusedHoistedLocals"/>.</summary>
    public static string PruneUnusedHoistedLocals(string source, Func<string, bool> isInteresting) =>
        FuzzInlinerAndSimplifier.PruneUnusedHoistedLocals(source, isInteresting);

    /// <summary>Forwards to <see cref="FuzzSpeculativeBatchBuilder.GenerateSpeculativeBatchCandidates"/>.</summary>
    public static SpeculativeCandidateBatch GenerateSpeculativeBatchCandidates(
        string kernelSource,
        ulong fuzzSeed = 1UL,
        int? inputSeed = null,
        int maxCandidates = 16,
        IReadOnlyList<IReadOnlyList<int>>? priorSuccessfulRemovals = null) =>
        FuzzSpeculativeBatchBuilder.GenerateSpeculativeBatchCandidates(kernelSource, fuzzSeed, inputSeed, maxCandidates, priorSuccessfulRemovals);

    /// <summary>Forwards to <see cref="FuzzSpeculativeBatchBuilder.SelectBestSpeculativeCandidate"/>.</summary>
    public static SpeculativeBatchCandidate? SelectBestSpeculativeCandidate(
        SpeculativeCandidateBatch batch,
        Func<SpeculativeBatchCandidate, bool> didCandidateFail) =>
        FuzzSpeculativeBatchBuilder.SelectBestSpeculativeCandidate(batch, didCandidateFail);

    internal static CompilationUnitSyntax ParseRoot(string source) => FuzzSyntaxHelpers.ParseRoot(source);

    internal static List<MethodDeclarationSyntax> GetKernelMethods(CompilationUnitSyntax root) =>
        FuzzSyntaxHelpers.GetKernelMethods(root);

    internal static List<MethodDeclarationSyntax> GetDeviceFunctionMethods(CompilationUnitSyntax root) =>
        FuzzSyntaxHelpers.GetDeviceFunctionMethods(root);

    internal static bool HasAttribute(MethodDeclarationSyntax method, string attributeShortName) =>
        FuzzSyntaxHelpers.HasAttribute(method, attributeShortName);

    internal static bool IsGidDeclaration(StatementSyntax stmt) => FuzzSyntaxHelpers.IsGidDeclaration(stmt);

    internal static bool IsGidBoundsGuard(StatementSyntax stmt) => FuzzSyntaxHelpers.IsGidBoundsGuard(stmt);

    internal static bool IsHashInitDeclaration(StatementSyntax stmt) => FuzzSyntaxHelpers.IsHashInitDeclaration(stmt);

    internal static bool IsFinalChecksumWrite(StatementSyntax stmt) => FuzzSyntaxHelpers.IsFinalChecksumWrite(stmt);

    internal static bool IsHoistedDefaultDeclaration(StatementSyntax stmt) =>
        FuzzSyntaxHelpers.IsHoistedDefaultDeclaration(stmt);

    internal static List<MethodDeclarationSyntax> CollectAllMethods(CompilationUnitSyntax root)
    {
        var list = new List<MethodDeclarationSyntax>();
        foreach (var node in root.DescendantNodes())
        {
            if (node is MethodDeclarationSyntax m)
            {
                list.Add(m);
            }
        }

        return list;
    }

    internal static MethodDeclarationSyntax? FindMethodByName(CompilationUnitSyntax root, string methodName)
    {
        foreach (var node in root.DescendantNodes())
        {
            if (node is MethodDeclarationSyntax m && string.Equals(m.Identifier.ValueText, methodName, StringComparison.Ordinal))
            {
                return m;
            }
        }

        return null;
    }

    private static MethodDeclarationSyntax HoistMethodLocals(
        CompilationUnitSyntax root,
        MethodDeclarationSyntax method,
        bool avoidStructConversionDefault)
    {
        if (method.Body is null)
        {
            return method;
        }

        var statements = method.Body.Statements;
        int prologueCount = FuzzSyntaxHelpers.CountPrologueStatements(statements);

        var hoistedDeclarations = new List<StatementSyntax>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var bodyStatements = new List<StatementSyntax>(statements.Count);

        for (int i = 0; i < prologueCount; i++)
        {
            bodyStatements.Add(statements[i]);
        }

        for (int i = prologueCount; i < statements.Count; i++)
        {
            var stmt = statements[i];
            if (CanHoistLocalDeclaration(root, stmt, avoidStructConversionDefault, out var localDecl))
            {
                HoistSingleLocalDeclaration(localDecl, seenNames, hoistedDeclarations, bodyStatements);
            }
            else
            {
                bodyStatements.Add(stmt);
            }
        }

        var combined = new List<StatementSyntax>(prologueCount + hoistedDeclarations.Count + bodyStatements.Count);
        for (int i = 0; i < prologueCount; i++)
        {
            combined.Add(bodyStatements[i]);
        }

        combined.AddRange(hoistedDeclarations);
        for (int i = prologueCount; i < bodyStatements.Count; i++)
        {
            combined.Add(bodyStatements[i]);
        }

        return method.WithBody(method.Body.WithStatements(SyntaxFactory.List(combined)));
    }

    private static bool CanHoistLocalDeclaration(
        CompilationUnitSyntax root,
        StatementSyntax stmt,
        bool avoidStructConversionDefault,
        [NotNullWhen(true)] out LocalDeclarationStatementSyntax? localDecl)
    {
        if (stmt is LocalDeclarationStatementSyntax ld &&
            !ld.Modifiers.Any(SyntaxKind.ConstKeyword) &&
            ld.Declaration.Type is not RefTypeSyntax &&
            !ld.Declaration.Type.IsVar &&
            !FuzzSyntaxHelpers.IsStackAllocSpanDeclaration(ld) &&
            !DeclaresLoopIndexVariable(ld) &&
            FuzzSyntaxHelpers.IsHoistableLocalType(root, ld.Declaration.Type, avoidStructConversionDefault))
        {
            localDecl = ld;
            return true;
        }

        localDecl = null;
        return false;
    }

    private static bool DeclaresLoopIndexVariable(LocalDeclarationStatementSyntax localDecl)
    {
        foreach (var v in localDecl.Declaration.Variables)
        {
            if (v.Identifier.ValueText.StartsWith("loopIdx", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void HoistSingleLocalDeclaration(
        LocalDeclarationStatementSyntax localDecl,
        HashSet<string> seenNames,
        List<StatementSyntax> hoistedDeclarations,
        List<StatementSyntax> bodyStatements)
    {
        if (FuzzSyntaxHelpers.IsHoistedDefaultDeclaration(localDecl))
        {
            string vName = localDecl.Declaration.Variables[0].Identifier.ValueText;
            if (seenNames.Add(vName))
            {
                hoistedDeclarations.Add(localDecl);
            }

            return;
        }

        string typeText = FuzzSyntaxHelpers.RenderNodeText(localDecl.Declaration.Type);
        foreach (var variable in localDecl.Declaration.Variables)
        {
            string name = variable.Identifier.ValueText;
            if (seenNames.Add(name))
            {
                var hoistedStmt = SyntaxFactory.ParseStatement($"{typeText} {name} = default({typeText});{Environment.NewLine}");
                hoistedDeclarations.Add(hoistedStmt);
            }

            if (variable.Initializer is not null)
            {
                string initValueText = FuzzSyntaxHelpers.RenderNodeText(variable.Initializer.Value);
                var assignStmt = SyntaxFactory.ParseStatement($"{name} = {initValueText};{Environment.NewLine}");
                bodyStatements.Add(assignStmt);
            }
        }
    }

    private static string CoarseRemoveStatementsInMethod(string source, string methodName, Func<string, bool> isInteresting)
    {
        string current = source;
        int granularity = 2;

        while (true)
        {
            var root = FuzzSyntaxHelpers.ParseRoot(current);
            var method = FindMethodByName(root, methodName);
            if (method?.Body is null)
            {
                break;
            }

            var removableIndices = FuzzSyntaxHelpers.GetRemovableStatementIndices(method.Body.Statements);
            int n = removableIndices.Count;
            if (n == 0)
            {
                break;
            }

            granularity = Math.Min(granularity, n);
            bool reducedInPass = false;
            int chunkSize = Math.Max(1, n / granularity);

            for (int chunkIndex = granularity - 1; chunkIndex >= 0; chunkIndex--)
            {
                int start = chunkIndex * chunkSize;
                int end = (chunkIndex == granularity - 1) ? n : Math.Min(n, (chunkIndex + 1) * chunkSize);
                if (start >= end)
                {
                    continue;
                }

                var toRemove = new HashSet<int>();
                for (int i = start; i < end; i++)
                {
                    toRemove.Add(removableIndices[i]);
                }

                string candidate = FuzzSyntaxHelpers.RemoveMethodStatementsByIndex(root, method, toRemove);
                if (isInteresting(candidate))
                {
                    current = candidate;
                    granularity = Math.Max(2, granularity - 1);
                    reducedInPass = true;
                    break;
                }
            }

            if (!reducedInPass)
            {
                if (granularity >= n)
                {
                    break;
                }

                granularity = Math.Min(n, granularity * 2);
            }
        }

        return current;
    }
}
