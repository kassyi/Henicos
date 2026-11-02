using System.Globalization;
using System.Linq;
using System.Text;
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
/// Roslyn syntax-tree-based reducer for failing differential fuzz kernels:
/// <list type="number">
/// <item><description><b>In-memory / single-candidate reduction</b>: Hoist all non-<see langword="ref"/> local variable declarations to <c>T v = default(T);</c> at the top of the method body, inline/prune helper <c>[GpuDeviceFunction]</c> methods, run binary-search coarse statement removal (<c>ddmin</c>), simplify expressions to a fixed point, and prune unused local declarations.</description></item>
/// <item><description><b>Batched CUDA reduction (16-candidate speculative batch reduction)</b>: Pack up to 16 independent reduced candidate kernels (<c>Candidate_00</c>..<c>Candidate_15</c>) into a single <c>.cu.cs</c> + <c>FuzzBatchCases.cs</c> so one <c>nvcc</c> build tests all 16 candidates simultaneously, plus intersection merging of disjoint successful chunk removals.</description></item>
/// </list>
/// </summary>
public static class FuzzReducer
{
    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    /// <summary>
    /// Reduces <paramref name="source"/> while <paramref name="isInteresting"/> returns <see langword="true"/>.
    /// </summary>
    public static FuzzReductionResult Reduce(string source, Func<string, bool> isInteresting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(isInteresting);

        int origStatements = CountKernelExecutableStatements(source);
        int origDeviceFuncs = CountDeviceFunctionMethods(source);
        int iterations = 0;

        bool TestCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !HasValidSyntax(candidate))
            {
                return false;
            }

            iterations++;
            return isInteresting(candidate);
        }

        string current = source;

        // Step 1: Hoist all non-ref local variable declarations to `T v = default(T);` at the top of each method body.
        string hoisted = HoistLocalsToDefault(current);
        if (!string.Equals(hoisted, current, StringComparison.Ordinal) && TestCandidate(hoisted))
        {
            current = hoisted;
        }

        // Step 2: Inline and prune [GpuDeviceFunction] helper methods.
        current = InlineAndPruneDeviceFunctions(current, TestCandidate);

        // Re-hoist locals in case inlining introduced new local declarations.
        string rehoisted = HoistLocalsToDefault(current);
        if (!string.Equals(rehoisted, current, StringComparison.Ordinal) && TestCandidate(rehoisted))
        {
            current = rehoisted;
        }

        // Step 3 & 4: Fixed-point loop of binary-search coarse statement removal, expression simplification, and unused local pruning.
        for (int pass = 0; pass < 6; pass++)
        {
            string beforePass = current;

            current = CoarseRemoveStatements(current, TestCandidate);
            current = InlineAndPruneDeviceFunctions(current, TestCandidate);
            current = SimplifyExpressions(current, TestCandidate);
            current = PruneUnusedHoistedLocals(current, TestCandidate);
            current = CollapseSingleAssignmentLocals(current, TestCandidate);
            current = PruneUnusedHoistedLocals(current, TestCandidate);

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
        var root = ParseRoot(source);
        var kernelMethods = GetKernelMethods(root);
        if (kernelMethods.Count == 0)
        {
            kernelMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        }

        int count = 0;
        foreach (var method in kernelMethods)
        {
            if (method.Body is null)
            {
                continue;
            }

            foreach (var stmt in method.Body.Statements)
            {
                if (IsHoistedDefaultDeclaration(stmt) && !IsHashInitDeclaration(stmt) && !IsGidDeclaration(stmt))
                {
                    continue;
                }

                count++;
            }
        }

        return count;
    }

    /// <summary>Counts the number of <c>[GpuDeviceFunction]</c> helper methods in <paramref name="source"/>.</summary>
    public static int CountDeviceFunctionMethods(string source)
    {
        var root = ParseRoot(source);
        return GetDeviceFunctionMethods(root).Count;
    }

    /// <summary>
    /// Hoists all non-<see langword="ref"/> local variable declarations inside methods to <c>T v = default(T);</c>
    /// right after the <c>gid</c> bounds guard (or at the top of the method body), converting original declaration sites
    /// <c>T v = expr;</c> into simple assignment statements <c>v = expr;</c> (Fuzzlyn <c>Reducer.CoarseSimplify</c>).
    /// </summary>
    public static string HoistLocalsToDefault(string source)
    {
        var root = ParseRoot(source);
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        if (methods.Count == 0)
        {
            return source;
        }

        var updatedRoot = root.ReplaceNodes(methods, static (_, currentMethod) => HoistMethodLocals(currentMethod));
        return updatedRoot.ToFullString();
    }

    private static MethodDeclarationSyntax HoistMethodLocals(MethodDeclarationSyntax method)
    {
        if (method.Body is null)
        {
            return method;
        }

        var statements = method.Body.Statements;
        int prologueCount = CountPrologueStatements(statements);

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
            if (stmt is LocalDeclarationStatementSyntax localDecl &&
                !localDecl.Modifiers.Any(SyntaxKind.ConstKeyword) &&
                localDecl.Declaration.Type is not RefTypeSyntax &&
                !localDecl.Declaration.Type.IsVar)
            {
                if (IsHoistedDefaultDeclaration(localDecl))
                {
                    string vName = localDecl.Declaration.Variables[0].Identifier.ValueText;
                    if (seenNames.Add(vName))
                    {
                        hoistedDeclarations.Add(localDecl);
                    }

                    continue;
                }

                var typeSyntax = localDecl.Declaration.Type.WithoutTrivia();
                foreach (var variable in localDecl.Declaration.Variables)
                {
                    string name = variable.Identifier.ValueText;
                    if (seenNames.Add(name))
                    {
                        var hoistedStmt = SyntaxFactory.ParseStatement($"        {typeSyntax} {name} = default({typeSyntax});{Environment.NewLine}");
                        hoistedDeclarations.Add(hoistedStmt);
                    }

                    if (variable.Initializer is not null)
                    {
                        string leading = stmt.GetLeadingTrivia().ToFullString();
                        if (string.IsNullOrEmpty(leading))
                        {
                            leading = "        ";
                        }

                        string trailing = stmt.GetTrailingTrivia().ToFullString();
                        if (string.IsNullOrEmpty(trailing))
                        {
                            trailing = Environment.NewLine;
                        }

                        var assignStmt = SyntaxFactory.ParseStatement($"{leading}{name} = {variable.Initializer.Value};{trailing}");
                        bodyStatements.Add(assignStmt);
                    }
                }
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

    /// <summary>
    /// Inlines calls to <c>[GpuDeviceFunction]</c> helper methods into their callers and removes unused
    /// <c>[GpuDeviceFunction]</c> methods whenever <paramref name="isInteresting"/> holds.
    /// </summary>
    public static string InlineAndPruneDeviceFunctions(string source, Func<string, bool> isInteresting)
    {
        string current = source;

        for (int round = 0; round < 8; round++)
        {
            string beforeRound = current;
            var root = ParseRoot(current);
            var helpers = GetDeviceFunctionMethods(root);
            if (helpers.Count == 0)
            {
                break;
            }

            // 1. Try removing any helper method directly if it is already unused (or if its call site can be simplified).
            foreach (var helper in helpers)
            {
                string helperName = helper.Identifier.ValueText;
                if (!IsHelperReferencedOutsideItself(root, helper))
                {
                    var withoutHelper = root.RemoveNode(helper, SyntaxRemoveOptions.KeepNoTrivia);
                    if (withoutHelper is not null)
                    {
                        string candidate = withoutHelper.ToFullString();
                        if (isInteresting(candidate))
                        {
                            current = candidate;
                            root = ParseRoot(current);
                        }
                    }
                }
            }

            // 2. Try inlining each remaining helper into its call sites (bottom-up: leaf helpers first).
            helpers = GetDeviceFunctionMethods(root);
            for (int h = helpers.Count - 1; h >= 0; h--)
            {
                var helper = helpers[h];
                string? inlinedCandidate = TryInlineHelperMethod(current, helper.Identifier.ValueText);
                if (inlinedCandidate is not null && !string.Equals(inlinedCandidate, current, StringComparison.Ordinal))
                {
                    if (isInteresting(inlinedCandidate))
                    {
                        current = inlinedCandidate;
                        break;
                    }
                }
            }

            if (string.Equals(current, beforeRound, StringComparison.Ordinal))
            {
                break;
            }
        }

        return current;
    }

    private static string? TryInlineHelperMethod(string source, string helperName)
    {
        var root = ParseRoot(source);
        var helper = GetDeviceFunctionMethods(root)
            .FirstOrDefault(m => string.Equals(m.Identifier.ValueText, helperName, StringComparison.Ordinal));
        if (helper is null)
        {
            return null;
        }

        string? returnExprTemplate = ExtractSingleReturnExpression(helper);
        if (returnExprTemplate is null)
        {
            return null;
        }

        var paramNames = helper.ParameterList.Parameters.Select(static p => p.Identifier.ValueText).ToArray();
        var paramTypes = helper.ParameterList.Parameters.Select(static p => p.Type?.ToString() ?? "int").ToArray();

        // Replace all invocations of `helperName(...)` outside `helper` itself.
        var invocations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => !helper.Span.Contains(inv.Span) && GetInvokedMethodName(inv) == helperName && inv.ArgumentList.Arguments.Count == paramNames.Length)
            .ToList();

        if (invocations.Count == 0)
        {
            var removedOnly = root.RemoveNode(helper, SyntaxRemoveOptions.KeepNoTrivia);
            return removedOnly?.ToFullString();
        }

        var replacedRoot = root.ReplaceNodes(invocations, (origInv, _) =>
        {
            string substituted = SubstituteParameters(
                returnExprTemplate,
                paramNames,
                paramTypes,
                origInv.ArgumentList.Arguments.Select(static a => a.Expression.ToString()).ToArray());
            return SyntaxFactory.ParseExpression($"({substituted})").WithTriviaFrom(origInv);
        });

        var helperInReplaced = GetDeviceFunctionMethods(replacedRoot)
            .FirstOrDefault(m => string.Equals(m.Identifier.ValueText, helperName, StringComparison.Ordinal));
        if (helperInReplaced is not null && !IsHelperReferencedOutsideItself(replacedRoot, helperInReplaced))
        {
            replacedRoot = replacedRoot.RemoveNode(helperInReplaced, SyntaxRemoveOptions.KeepNoTrivia) ?? replacedRoot;
        }

        return replacedRoot.ToFullString();
    }

    private static string? ExtractSingleReturnExpression(MethodDeclarationSyntax helper)
    {
        if (helper.ExpressionBody is not null)
        {
            return helper.ExpressionBody.Expression.ToString();
        }

        if (helper.Body is null)
        {
            return null;
        }

        // Allow local variable assignments / declarations before a final `return expr;` by substituting them forward into the return expression.
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var stmt in helper.Body.Statements)
        {
            if (stmt is LocalDeclarationStatementSyntax localDecl && localDecl.Declaration.Type is not null)
            {
                string typeText = localDecl.Declaration.Type.ToString();
                foreach (var v in localDecl.Declaration.Variables)
                {
                    string vName = v.Identifier.ValueText;
                    string initText = v.Initializer is not null
                        ? SubstituteUsingMap(v.Initializer.Value.ToString(), env)
                        : $"default({typeText})";
                    env[vName] = $"(({typeText})({initText}))";
                }
            }
            else if (stmt is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assign } &&
                     assign.Left is IdentifierNameSyntax idLeft)
            {
                string targetName = idLeft.Identifier.ValueText;
                string rhsText = SubstituteUsingMap(assign.Right.ToString(), env);
                if (assign.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    env[targetName] = $"({rhsText})";
                }
                else
                {
                    string prev = env.TryGetValue(targetName, out string? existing) ? existing : targetName;
                    string binOp = assign.OperatorToken.ValueText[..^1];
                    env[targetName] = $"({prev} {binOp} ({rhsText}))";
                }
            }
            else if (stmt is ReturnStatementSyntax { Expression: { } retExpr })
            {
                return SubstituteUsingMap(retExpr.ToString(), env);
            }
            else
            {
                return null;
            }
        }

        return null;
    }

    private static string SubstituteParameters(
        string template,
        IReadOnlyList<string> paramNames,
        IReadOnlyList<string> paramTypes,
        IReadOnlyList<string> argExprs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < paramNames.Count; i++)
        {
            map[paramNames[i]] = $"(({paramTypes[i]})({argExprs[i]}))";
        }

        return SubstituteUsingMap(template, map);
    }

    private static string SubstituteUsingMap(string exprText, IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0)
        {
            return exprText;
        }

        var expr = SyntaxFactory.ParseExpression(exprText);
        var ids = expr.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Where(id => !IsMemberAccessRight(id) && map.ContainsKey(id.Identifier.ValueText))
            .ToList();

        if (ids.Count == 0)
        {
            return exprText;
        }

        var replaced = expr.ReplaceNodes(ids, (orig, _) =>
            SyntaxFactory.ParseExpression(map[orig.Identifier.ValueText]).WithTriviaFrom(orig));
        return replaced.ToFullString();
    }

    /// <summary>
    /// Binary-search coarse statement remover (<c>CoarseStatementRemover</c> / <c>ddmin</c>).
    /// Preserves the <c>gid</c> bounds prologue, hoisted <c>T v = default(T);</c> declarations, <c>ulong hash = ...;</c> initialization,
    /// and final <c>checksum[gid] = hash;</c> write while deleting removable statements in binary chunks.
    /// </summary>
    public static string CoarseRemoveStatements(string source, Func<string, bool> isInteresting)
    {
        string current = source;
        var root = ParseRoot(current);
        var kernelMethods = GetKernelMethods(root);
        if (kernelMethods.Count == 0)
        {
            kernelMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        }

        for (int mIndex = 0; mIndex < kernelMethods.Count; mIndex++)
        {
            string methodName = kernelMethods[mIndex].Identifier.ValueText;
            current = CoarseRemoveStatementsInMethod(current, methodName, isInteresting);
        }

        return current;
    }

    private static string CoarseRemoveStatementsInMethod(string source, string methodName, Func<string, bool> isInteresting)
    {
        string current = source;
        int granularity = 2;

        while (true)
        {
            var root = ParseRoot(current);
            var method = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => string.Equals(m.Identifier.ValueText, methodName, StringComparison.Ordinal));
            if (method?.Body is null)
            {
                break;
            }

            var removableIndices = GetRemovableStatementIndices(method.Body.Statements);
            int n = removableIndices.Count;
            if (n == 0)
            {
                break;
            }

            if (granularity > n)
            {
                granularity = n;
            }

            bool reducedInPass = false;
            int chunkSize = Math.Max(1, n / granularity);

            // Try removing from the back first (like Fuzzlyn CoarseStatementRemover), then earlier chunks.
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

                string candidate = RemoveMethodStatementsByIndex(root, method, toRemove);
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

    /// <summary>
    /// Fine-grained expression simplifier: replaces ternary, binary, and cast expressions with simpler sub-expressions
    /// or constants while <paramref name="isInteresting"/> holds.
    /// </summary>
    public static string SimplifyExpressions(string source, Func<string, bool> isInteresting)
    {
        string current = source;

        for (int pass = 0; pass < 4; pass++)
        {
            string beforePass = current;
            var root = ParseRoot(current);
            var kernelMethods = GetKernelMethods(root);
            if (kernelMethods.Count == 0)
            {
                break;
            }

            var method = kernelMethods[0];
            if (method.Body is null)
            {
                break;
            }

            var removableIndices = GetRemovableStatementIndices(method.Body.Statements);
            foreach (int stmtIdx in removableIndices)
            {
                root = ParseRoot(current);
                method = GetKernelMethods(root).FirstOrDefault();
                if (method?.Body is null || stmtIdx >= method.Body.Statements.Count)
                {
                    break;
                }

                var stmt = method.Body.Statements[stmtIdx];
                if (IsChecksumFoldStatement(stmt))
                {
                    continue;
                }

                // Try replacing binary/conditional sub-expressions with their left or right branch.
                var candidateExprs = stmt.DescendantNodes()
                    .OfType<ExpressionSyntax>()
                    .Where(static e => e is BinaryExpressionSyntax or ConditionalExpressionSyntax)
                    .ToList();

                foreach (var expr in candidateExprs)
                {
                    IReadOnlyList<ExpressionSyntax> replacements = expr switch
                    {
                        ConditionalExpressionSyntax cond => [cond.WhenTrue, cond.WhenFalse],
                        BinaryExpressionSyntax bin when !IsBooleanComparison(bin) => [bin.Left, bin.Right],
                        _ => [],
                    };

                    bool simplifiedStmt = false;
                    foreach (var rep in replacements)
                    {
                        var newStmt = stmt.ReplaceNode(expr, SyntaxFactory.ParenthesizedExpression(rep.WithoutTrivia()));
                        var newRoot = root.ReplaceNode(stmt, newStmt);
                        string candidate = newRoot.ToFullString();
                        if (isInteresting(candidate))
                        {
                            current = candidate;
                            simplifiedStmt = true;
                            break;
                        }
                    }

                    if (simplifiedStmt)
                    {
                        break;
                    }
                }
            }

            if (string.Equals(current, beforePass, StringComparison.Ordinal))
            {
                break;
            }
        }

        return current;
    }

    /// <summary>
    /// Prunes hoisted <c>T v = default(T);</c> local declarations whose variable is never referenced in the rest of the method body.
    /// </summary>
    public static string PruneUnusedHoistedLocals(string source, Func<string, bool> isInteresting)
    {
        var root = ParseRoot(source);
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        if (methods.Count == 0)
        {
            return source;
        }

        string current = source;
        foreach (var m in methods)
        {
            string methodName = m.Identifier.ValueText;
            var latestRoot = ParseRoot(current);
            var method = latestRoot.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(x => string.Equals(x.Identifier.ValueText, methodName, StringComparison.Ordinal));
            if (method?.Body is null)
            {
                continue;
            }

            var statements = method.Body.Statements;
            var unusedIndices = new HashSet<int>();
            for (int i = 0; i < statements.Count; i++)
            {
                if (statements[i] is LocalDeclarationStatementSyntax localDecl &&
                    IsHoistedDefaultDeclaration(localDecl) &&
                    localDecl.Declaration.Variables.Count == 1)
                {
                    string varName = localDecl.Declaration.Variables[0].Identifier.ValueText;
                    bool usedLater = false;
                    for (int j = 0; j < statements.Count; j++)
                    {
                        if (j == i)
                        {
                            continue;
                        }

                        if (statements[j].DescendantNodes().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.ValueText == varName))
                        {
                            usedLater = true;
                            break;
                        }
                    }

                    if (!usedLater)
                    {
                        unusedIndices.Add(i);
                    }
                }
            }

            if (unusedIndices.Count > 0)
            {
                string candidate = RemoveMethodStatementsByIndex(latestRoot, method, unusedIndices);
                if (isInteresting(candidate))
                {
                    current = candidate;
                }
            }
        }

        return current;
    }

    private static string CollapseSingleAssignmentLocals(string source, Func<string, bool> isInteresting)
    {
        string current = source;

        for (int round = 0; round < 8; round++)
        {
            var root = ParseRoot(current);
            var method = GetKernelMethods(root).FirstOrDefault();
            if (method?.Body is null)
            {
                break;
            }

            var statements = method.Body.Statements;
            bool changed = false;

            // Find a local `v` that is assigned once (`v = expr;`) and then read once in another statement, and try inlining `expr` into the reader.
            for (int i = 0; i < statements.Count; i++)
            {
                if (statements[i] is not ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression, Left: IdentifierNameSyntax leftId } assign })
                {
                    continue;
                }

                string varName = leftId.Identifier.ValueText;
                if (varName is "gid" or "hash")
                {
                    continue;
                }

                // Ensure `varName` is not referenced on the RHS of its own assignment and has no other writes.
                if (assign.Right.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.ValueText == varName))
                {
                    continue;
                }

                var otherStmtsWithVar = new List<int>();
                for (int j = 0; j < statements.Count; j++)
                {
                    if (j == i || IsHoistedDefaultDeclaration(statements[j]))
                    {
                        continue;
                    }

                    if (statements[j].DescendantNodes().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.ValueText == varName))
                    {
                        otherStmtsWithVar.Add(j);
                    }
                }

                if (otherStmtsWithVar.Count == 1 && otherStmtsWithVar[0] > i)
                {
                    int readerIdx = otherStmtsWithVar[0];
                    var readerStmt = statements[readerIdx];
                    var readerRefs = readerStmt.DescendantNodes()
                        .OfType<IdentifierNameSyntax>()
                        .Where(id => id.Identifier.ValueText == varName && !IsAssignedOrMutatedTarget(id))
                        .ToList();

                    if (readerRefs.Count == 1)
                    {
                        string? declaredType = FindHoistedLocalType(statements, varName);
                        string replacementExprText = declaredType is not null
                            ? $"(({declaredType})({assign.Right}))"
                            : $"({assign.Right})";
                        var newReaderStmt = readerStmt.ReplaceNode(
                            readerRefs[0],
                            SyntaxFactory.ParseExpression(replacementExprText).WithTriviaFrom(readerRefs[0]));

                        var newStmts = new List<StatementSyntax>(statements.Count - 1);
                        for (int k = 0; k < statements.Count; k++)
                        {
                            if (k == i)
                            {
                                continue;
                            }

                            newStmts.Add(k == readerIdx ? newReaderStmt : statements[k]);
                        }

                        var newMethod = method.WithBody(method.Body.WithStatements(SyntaxFactory.List(newStmts)));
                        string candidate = root.ReplaceNode(method, newMethod).ToFullString();
                        if (isInteresting(candidate))
                        {
                            current = candidate;
                            changed = true;
                            break;
                        }
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return current;
    }

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

        string hoistedSource = HoistLocalsToDefault(kernelSource);
        var root = ParseRoot(hoistedSource);
        var kernelMethod = GetKernelMethods(root).FirstOrDefault()
            ?? root.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException("No kernel method found in source.");

        if (kernelMethod.Body is null)
        {
            throw new InvalidOperationException("Kernel method has no body.");
        }

        string baseKernelName = kernelMethod.Identifier.ValueText;
        var removableIndices = GetRemovableStatementIndices(kernelMethod.Body.Statements);
        var candidates = new List<SpeculativeBatchCandidate>(maxCandidates);
        var seenSources = new HashSet<string>(StringComparer.Ordinal);

        void TryAddCandidate(string description, IReadOnlyList<int> removedIndices, MethodDeclarationSyntax mutatedMethod)
        {
            if (candidates.Count >= maxCandidates)
            {
                return;
            }

            int idx = candidates.Count;
            string candName = string.Create(CultureInfo.InvariantCulture, $"Candidate_{idx:D2}");
            var renamedMethod = mutatedMethod.WithIdentifier(SyntaxFactory.Identifier(candName));
            string methodSource = renamedMethod.ToFullString();

            // Deduplicate by body text so every slot in the 16-candidate batch tests a distinct hypothesis.
            string bodyKey = renamedMethod.Body?.ToFullString() ?? methodSource;
            if (!seenSources.Add(bodyKey))
            {
                return;
            }

            string caseSource = RenderCandidateCaseProperty(candName, fuzzSeed, inputSeed);
            int stmtCount = CountMethodExecutableStatements(renamedMethod);
            candidates.Add(new SpeculativeBatchCandidate(
                idx,
                candName,
                description,
                methodSource,
                caseSource,
                removedIndices,
                stmtCount));
        }

        // 0. If multiple disjoint removals succeeded in the previous batch, place their union (intersection of kept statements) first!
        if (priorSuccessfulRemovals is { Count: >= 2 })
        {
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
                var mergedMethod = RemoveStatementsFromMethodNode(kernelMethod, combinedRemove);
                TryAddCandidate("IntersectionMerge", combinedRemove.ToArray(), mergedMethod);
            }
        }

        int n = removableIndices.Count;
        if (n > 0)
        {
            // 1. 4 quarter-chunk removals (or up to n chunks when n < 4).
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

                var mutated = RemoveStatementsFromMethodNode(kernelMethod, new HashSet<int>(rem));
                TryAddCandidate($"Chunk4_{c}", rem, mutated);
            }

            // 2. 2 half-chunk removals (back half, front half).
            if (n >= 2)
            {
                int mid = n / 2;
                var backHalf = removableIndices.Skip(mid).ToArray();
                var frontHalf = removableIndices.Take(mid).ToArray();
                TryAddCandidate("Half_Back", backHalf, RemoveStatementsFromMethodNode(kernelMethod, new HashSet<int>(backHalf)));
                TryAddCandidate("Half_Front", frontHalf, RemoveStatementsFromMethodNode(kernelMethod, new HashSet<int>(frontHalf)));
            }

            // 3. Individual statement removals / expression simplifications up to 16 candidates total.
            for (int i = n - 1; i >= 0 && candidates.Count < maxCandidates; i--)
            {
                int stmtIdx = removableIndices[i];
                var singleRem = new[] { stmtIdx };
                var mutated = RemoveStatementsFromMethodNode(kernelMethod, new HashSet<int>(singleRem));
                TryAddCandidate($"RemoveStmt_{stmtIdx}", singleRem, mutated);
            }

            // 4. Fill any remaining slots with expression operand simplifications.
            for (int i = 0; i < n && candidates.Count < maxCandidates; i++)
            {
                int stmtIdx = removableIndices[i];
                var stmt = kernelMethod.Body.Statements[stmtIdx];
                if (IsChecksumFoldStatement(stmt))
                {
                    continue;
                }

                var binOrCond = stmt.DescendantNodes()
                    .OfType<ExpressionSyntax>()
                    .FirstOrDefault(static e => e is ConditionalExpressionSyntax or BinaryExpressionSyntax);

                if (binOrCond is ConditionalExpressionSyntax cond)
                {
                    var newStmtTrue = stmt.ReplaceNode(cond, SyntaxFactory.ParenthesizedExpression(cond.WhenTrue.WithoutTrivia()));
                    TryAddCandidate($"CondTrue_{stmtIdx}", [], ReplaceSingleStatement(kernelMethod, stmtIdx, newStmtTrue));
                    var newStmtFalse = stmt.ReplaceNode(cond, SyntaxFactory.ParenthesizedExpression(cond.WhenFalse.WithoutTrivia()));
                    TryAddCandidate($"CondFalse_{stmtIdx}", [], ReplaceSingleStatement(kernelMethod, stmtIdx, newStmtFalse));
                }
                else if (binOrCond is BinaryExpressionSyntax bin && !IsBooleanComparison(bin))
                {
                    var newStmtLeft = stmt.ReplaceNode(bin, SyntaxFactory.ParenthesizedExpression(bin.Left.WithoutTrivia()));
                    TryAddCandidate($"BinLeft_{stmtIdx}", [], ReplaceSingleStatement(kernelMethod, stmtIdx, newStmtLeft));
                }
            }
        }

        // Ensure at least 1 candidate exists even if the kernel is already minimal.
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
    /// picking the smallest candidate that still reproduces the failure (and preferring the intersection merge if multiple disjoint chunk removals succeeded).
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

    private static string BuildSpeculativeKernelsCompilationUnit(
        CompilationUnitSyntax originalRoot,
        IReadOnlyList<SpeculativeBatchCandidate> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS0162, CS0219, CS1717, CS1718");
        sb.AppendLine("using System;");
        sb.AppendLine("using Kassyi.GSharp.Core;");
        sb.AppendLine();
        sb.AppendLine("namespace Kassyi.GSharp.Tests.Shared.Differential;");
        sb.AppendLine();

        var existingEnums = originalRoot.DescendantNodes().OfType<EnumDeclarationSyntax>().ToList();
        if (existingEnums.Count > 0)
        {
            foreach (var enumDecl in existingEnums)
            {
                sb.AppendLine(enumDecl.ToFullString().Trim());
                sb.AppendLine();
            }
        }
        else
        {
            ScalarFuzzGenerator.AppendEnumDeclarations(sb);
            sb.AppendLine();
        }

        sb.AppendLine("public static partial class FuzzBatchKernels");
        sb.AppendLine("{");

        // Include any [GpuDeviceFunction] helpers still present in originalRoot so candidates calling them compile.
        foreach (var helper in GetDeviceFunctionMethods(originalRoot))
        {
            sb.AppendLine(helper.ToFullString().TrimEnd());
            sb.AppendLine();
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine($"    // Speculative candidate #{candidates[i].Index:D2}: {candidates[i].CandidateDescription}");
            sb.AppendLine(candidates[i].CandidateKernelMethodSource.TrimEnd());
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildSpeculativeCasesCompilationUnit(
        IReadOnlyList<SpeculativeBatchCandidate> candidates,
        ulong fuzzSeed,
        int? inputSeed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Globalization;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using Kassyi.GSharp.Core;");
        sb.AppendLine();
        sb.AppendLine("namespace Kassyi.GSharp.Tests.Shared.Differential;");
        sb.AppendLine();
        sb.AppendLine("public static class FuzzBatchCases");
        sb.AppendLine("{");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    public const ulong BatchFuzzSeed = {fuzzSeed}UL;"));
        sb.AppendLine();

        foreach (var c in candidates)
        {
            sb.AppendLine(c.CandidateCasePropertySource);
            sb.AppendLine();
        }

        sb.AppendLine("    public static IReadOnlyList<(string KernelName, ulong KernelSeed, DifferentialCase Case)> All { get; } =");
        sb.AppendLine("    [");
        foreach (var c in candidates)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"        (\"{c.CandidateKernelName}\", {fuzzSeed}UL, {c.CandidateKernelName}_Case),"));
        }

        sb.AppendLine("    ];");
        sb.AppendLine();
        ScalarFuzzGenerator.AppendBatchRunnerMain(sb);
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string RenderCandidateCaseProperty(string candidateKernelName, ulong fuzzSeed, int? inputSeed)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    // fuzz-seed: {FuzzRandom.FormatSeed(fuzzSeed)}"));
        sb.AppendLine($"    public static DifferentialCase {candidateKernelName}_Case {{ get; }} = new(\"FuzzBatchKernels.{candidateKernelName}\")");
        sb.AppendLine("    {");
        sb.AppendLine("        Shape = LaunchShapes.PartialWarp(),");
        if (inputSeed is { } explicitInputSeed)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"        Seeds = [{explicitInputSeed}],"));
        }

        sb.AppendLine("        Inputs = static (rng, shape) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            int rows = shape.Rows;");
        sb.AppendLine("            var (inA, inB) = DiffValues.Int32Pairs(rng, rows);");
        sb.AppendLine("            var inC = DiffValues.Values(rng, rows, DiffValues.ShiftCounts, DiffValues.NextInt32);");
        sb.AppendLine("            return new DiffBuffers()");
        sb.AppendLine("                .In(\"inA\", inA)");
        sb.AppendLine("                .In(\"inB\", inB)");
        sb.AppendLine("                .In(\"inC\", inC)");
        sb.AppendLine("                .Out<ulong>(\"checksum\", rows, [\"hash\"], OutputPolicy.Exact);");
        sb.AppendLine("        },");
        sb.AppendLine($"        Launch = static (b, s) => FuzzBatchKernels.Launch{candidateKernelName}(");
        sb.AppendLine("            b.Get<int>(\"inA\"),");
        sb.AppendLine("            b.Get<int>(\"inB\"),");
        sb.AppendLine("            b.Get<int>(\"inC\"),");
        sb.AppendLine("            b.Get<ulong>(\"checksum\"),");
        sb.AppendLine("            gridDimX: s.GridX,");
        sb.AppendLine("            blockDimX: s.BlockX),");
        sb.AppendLine($"        Oracle = static (b, _) => FuzzBatchKernels.{candidateKernelName}(");
        sb.AppendLine("            b.Get<int>(\"inA\"),");
        sb.AppendLine("            b.Get<int>(\"inB\"),");
        sb.AppendLine("            b.Get<int>(\"inC\"),");
        sb.AppendLine("            b.Get<ulong>(\"checksum\")),");
        sb.Append("    };");
        return sb.ToString();
    }

    private static MethodDeclarationSyntax RemoveStatementsFromMethodNode(MethodDeclarationSyntax method, IReadOnlySet<int> indicesToRemove)
    {
        if (method.Body is null)
        {
            return method;
        }

        var kept = new List<StatementSyntax>(method.Body.Statements.Count);
        for (int i = 0; i < method.Body.Statements.Count; i++)
        {
            if (!indicesToRemove.Contains(i))
            {
                kept.Add(method.Body.Statements[i]);
            }
        }

        return method.WithBody(method.Body.WithStatements(SyntaxFactory.List(kept)));
    }

    private static MethodDeclarationSyntax ReplaceSingleStatement(MethodDeclarationSyntax method, int stmtIdx, StatementSyntax replacement)
    {
        if (method.Body is null)
        {
            return method;
        }

        var list = method.Body.Statements.ToList();
        list[stmtIdx] = replacement;
        return method.WithBody(method.Body.WithStatements(SyntaxFactory.List(list)));
    }

    private static int CountMethodExecutableStatements(MethodDeclarationSyntax method)
    {
        if (method.Body is null)
        {
            return 0;
        }

        int count = 0;
        foreach (var stmt in method.Body.Statements)
        {
            if (IsHoistedDefaultDeclaration(stmt) && !IsHashInitDeclaration(stmt) && !IsGidDeclaration(stmt))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    internal static CompilationUnitSyntax ParseRoot(string source) =>
        (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(source, s_parseOptions).GetRoot();

    internal static List<MethodDeclarationSyntax> GetKernelMethods(CompilationUnitSyntax root) =>
        root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(static m => HasAttribute(m, "GpuKernel"))
            .ToList();

    internal static List<MethodDeclarationSyntax> GetDeviceFunctionMethods(CompilationUnitSyntax root) =>
        root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(static m => HasAttribute(m, "GpuDeviceFunction"))
            .ToList();

    internal static bool HasAttribute(MethodDeclarationSyntax method, string attributeShortName)
    {
        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                string name = attr.Name.ToString();
                if (string.Equals(name, attributeShortName, StringComparison.Ordinal) ||
                    string.Equals(name, $"{attributeShortName}Attribute", StringComparison.Ordinal) ||
                    name.EndsWith($".{attributeShortName}", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountPrologueStatements(SyntaxList<StatementSyntax> statements)
    {
        int count = 0;
        if (statements.Count > 0 && IsGidDeclaration(statements[0]))
        {
            count = 1;
            if (statements.Count > 1 && IsGidBoundsGuard(statements[1]))
            {
                count = 2;
            }
        }

        return count;
    }

    private static List<int> GetRemovableStatementIndices(SyntaxList<StatementSyntax> statements)
    {
        var indices = new List<int>(statements.Count);
        for (int i = 0; i < statements.Count; i++)
        {
            var stmt = statements[i];
            if (IsGidDeclaration(stmt) ||
                IsGidBoundsGuard(stmt) ||
                IsHashInitDeclaration(stmt) ||
                IsFinalChecksumWrite(stmt) ||
                IsHoistedDefaultDeclaration(stmt))
            {
                continue;
            }

            indices.Add(i);
        }

        return indices;
    }

    private static string RemoveMethodStatementsByIndex(
        CompilationUnitSyntax root,
        MethodDeclarationSyntax method,
        HashSet<int> indicesToRemove)
    {
        var updatedMethod = RemoveStatementsFromMethodNode(method, indicesToRemove);
        return root.ReplaceNode(method, updatedMethod).ToFullString();
    }

    internal static bool IsGidDeclaration(StatementSyntax stmt) =>
        stmt is LocalDeclarationStatementSyntax localDecl &&
        localDecl.Declaration.Variables.Any(static v => v.Identifier.ValueText == "gid");

    internal static bool IsGidBoundsGuard(StatementSyntax stmt) =>
        stmt is IfStatementSyntax ifStmt &&
        ifStmt.Condition.ToString().Contains("gid", StringComparison.Ordinal) &&
        ifStmt.Statement.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Any();

    internal static bool IsHashInitDeclaration(StatementSyntax stmt) =>
        stmt is LocalDeclarationStatementSyntax localDecl &&
        localDecl.Declaration.Variables.Any(static v => v.Identifier.ValueText == "hash");

    internal static bool IsFinalChecksumWrite(StatementSyntax stmt) =>
        stmt is ExpressionStatementSyntax
        {
            Expression: AssignmentExpressionSyntax
            {
                Left: ElementAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "checksum" } },
            },
        };

    internal static bool IsHoistedDefaultDeclaration(StatementSyntax stmt)
    {
        if (stmt is not LocalDeclarationStatementSyntax localDecl ||
            localDecl.Declaration.Variables.Count != 1)
        {
            return false;
        }

        var v = localDecl.Declaration.Variables[0];
        if (v.Initializer is null)
        {
            return true;
        }

        return v.Initializer.Value is DefaultExpressionSyntax or LiteralExpressionSyntax { RawKind: (int)SyntaxKind.DefaultLiteralExpression };
    }

    private static bool IsChecksumFoldStatement(StatementSyntax stmt) =>
        stmt is ExpressionStatementSyntax
        {
            Expression: AssignmentExpressionSyntax
            {
                Left: IdentifierNameSyntax { Identifier.ValueText: "hash" },
            },
        };

    private static string? FindHoistedLocalType(SyntaxList<StatementSyntax> statements, string varName)
    {
        foreach (var stmt in statements)
        {
            if (stmt is LocalDeclarationStatementSyntax localDecl &&
                localDecl.Declaration.Variables.Any(v => v.Identifier.ValueText == varName))
            {
                return localDecl.Declaration.Type.ToString();
            }
        }

        return null;
    }

    private static bool IsAssignedOrMutatedTarget(IdentifierNameSyntax id)
    {
        return id.Parent switch
        {
            AssignmentExpressionSyntax assign => assign.Left == id,
            PrefixUnaryExpressionSyntax prefix => prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression),
            PostfixUnaryExpressionSyntax postfix => postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression),
            ArgumentSyntax arg => !arg.RefKindKeyword.IsKind(SyntaxKind.None),
            _ => false,
        };
    }

    private static bool IsBooleanComparison(BinaryExpressionSyntax bin) =>
        bin.Kind() is SyntaxKind.EqualsExpression
            or SyntaxKind.NotEqualsExpression
            or SyntaxKind.LessThanExpression
            or SyntaxKind.LessThanOrEqualExpression
            or SyntaxKind.GreaterThanExpression
            or SyntaxKind.GreaterThanOrEqualExpression
            or SyntaxKind.LogicalAndExpression
            or SyntaxKind.LogicalOrExpression;

    private static bool IsHelperReferencedOutsideItself(CompilationUnitSyntax root, MethodDeclarationSyntax helper)
    {
        string name = helper.Identifier.ValueText;
        return root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => !helper.Span.Contains(inv.Span) && GetInvokedMethodName(inv) == name);
    }

    private static string? GetInvokedMethodName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => null,
    };

    private static bool IsMemberAccessRight(IdentifierNameSyntax id) =>
        id.Parent is MemberAccessExpressionSyntax member && member.Name == id;

    private static bool HasValidSyntax(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, s_parseOptions);
        return !tree.GetDiagnostics().Any(static d => d.Severity == DiagnosticSeverity.Error);
    }
}
