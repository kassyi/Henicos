using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Performs <c>[GpuDeviceFunction]</c> inlining/pruning, fine-grained expression simplification,
/// unused hoisted local pruning, and single-assignment local collapsing during <see cref="FuzzReducer"/> runs.
/// </summary>
public static class FuzzInlinerAndSimplifier
{
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
            var root = FuzzSyntaxHelpers.ParseRoot(current);
            var helpers = FuzzSyntaxHelpers.GetDeviceFunctionMethods(root);
            if (helpers.Count == 0)
            {
                break;
            }

            current = TryRemoveUnreferencedHelpers(current, helpers, isInteresting);
            current = TryInlineRemainingHelpersBottomUp(current, isInteresting);

            if (string.Equals(current, beforeRound, StringComparison.Ordinal))
            {
                break;
            }
        }

        return current;
    }

    /// <summary>
    /// Fine-grained expression simplifier: replaces ternary and binary expressions with simpler sub-expressions
    /// while <paramref name="isInteresting"/> holds.
    /// </summary>
    public static string SimplifyExpressions(string source, Func<string, bool> isInteresting)
    {
        string current = source;

        for (int pass = 0; pass < 4; pass++)
        {
            string beforePass = current;
            var root = FuzzSyntaxHelpers.ParseRoot(current);
            var kernels = FuzzSyntaxHelpers.GetKernelMethods(root);
            if (kernels.Count == 0 || kernels[0].Body is null)
            {
                break;
            }

            var removableIndices = FuzzSyntaxHelpers.GetRemovableStatementIndices(kernels[0].Body!.Statements);
            foreach (int stmtIdx in removableIndices)
            {
                current = TrySimplifyStatementAt(current, stmtIdx, isInteresting);
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
        var root = FuzzSyntaxHelpers.ParseRoot(source);
        var methods = FuzzReducer.CollectAllMethods(root);
        if (methods.Count == 0)
        {
            return source;
        }

        string current = source;
        foreach (var m in methods)
        {
            string methodName = m.Identifier.ValueText;
            var latestRoot = FuzzSyntaxHelpers.ParseRoot(current);
            var method = FuzzReducer.FindMethodByName(latestRoot, methodName);
            if (method?.Body is null)
            {
                continue;
            }

            var unusedIndices = FindUnusedHoistedLocalStatementIndices(method.Body.Statements);
            if (unusedIndices.Count > 0)
            {
                string candidate = FuzzSyntaxHelpers.RemoveMethodStatementsByIndex(latestRoot, method, unusedIndices);
                if (isInteresting(candidate))
                {
                    current = candidate;
                }
            }
        }

        return current;
    }

    /// <summary>
    /// Collapses single-assignment local variables (<c>v = expr;</c> read once in a subsequent statement) by inlining <c>expr</c> into the reader.
    /// </summary>
    public static string CollapseSingleAssignmentLocals(string source, Func<string, bool> isInteresting)
    {
        string current = source;

        for (int round = 0; round < 8; round++)
        {
            var root = FuzzSyntaxHelpers.ParseRoot(current);
            var kernels = FuzzSyntaxHelpers.GetKernelMethods(root);
            if (kernels.Count == 0 || kernels[0].Body is null)
            {
                break;
            }

            if (!TryCollapseOneSingleAssignmentLocal(root, kernels[0], isInteresting, out string? updated))
            {
                break;
            }

            current = updated;
        }

        return current;
    }

    private static string TryRemoveUnreferencedHelpers(
        string current,
        List<MethodDeclarationSyntax> helpers,
        Func<string, bool> isInteresting)
    {
        var root = FuzzSyntaxHelpers.ParseRoot(current);
        var unreferenced = new List<MethodDeclarationSyntax>();
        foreach (var helper in helpers)
        {
            if (!FuzzSyntaxHelpers.IsHelperReferencedOutsideItself(root, helper))
            {
                unreferenced.Add(helper);
            }
        }

        if (unreferenced.Count > 1)
        {
            var withoutAll = root.RemoveNodes(unreferenced, SyntaxRemoveOptions.KeepNoTrivia);
            if (withoutAll is not null)
            {
                string bulkCandidate = FuzzSyntaxHelpers.RenderFullNode(withoutAll);
                if (isInteresting(bulkCandidate))
                {
                    return bulkCandidate;
                }
            }
        }

        foreach (var helper in helpers)
        {
            if (FuzzSyntaxHelpers.IsHelperReferencedOutsideItself(root, helper))
            {
                continue;
            }

            var withoutHelper = root.RemoveNode(helper, SyntaxRemoveOptions.KeepNoTrivia);
            if (withoutHelper is null)
            {
                continue;
            }

            string candidate = FuzzSyntaxHelpers.RenderFullNode(withoutHelper);
            if (isInteresting(candidate))
            {
                current = candidate;
                root = FuzzSyntaxHelpers.ParseRoot(current);
            }
        }

        return current;
    }

    private static string TryInlineRemainingHelpersBottomUp(string current, Func<string, bool> isInteresting)
    {
        var root = FuzzSyntaxHelpers.ParseRoot(current);
        var helpers = FuzzSyntaxHelpers.GetDeviceFunctionMethods(root);
        for (int h = helpers.Count - 1; h >= 0; h--)
        {
            string? inlinedCandidate = TryInlineHelperMethod(current, helpers[h].Identifier.ValueText);
            if (inlinedCandidate is not null &&
                !string.Equals(inlinedCandidate, current, StringComparison.Ordinal) &&
                isInteresting(inlinedCandidate))
            {
                return inlinedCandidate;
            }
        }

        return current;
    }

    private static string? TryInlineHelperMethod(string source, string helperName)
    {
        var root = FuzzSyntaxHelpers.ParseRoot(source);
        var helper = FindDeviceFunctionByName(root, helperName);
        if (helper is null || helper.TypeParameterList is not null || HasByRefParameters(helper))
        {
            return null;
        }

        string? returnExprTemplate = ExtractSingleReturnExpression(helper);
        if (returnExprTemplate is null)
        {
            return null;
        }

        var (paramNames, paramTypes) = ExtractParameters(helper);
        var invocations = CollectMatchingInvocations(root, helper, helperName, paramNames.Length);
        if (invocations.Count == 0)
        {
            var removed = root.RemoveNode(helper, SyntaxRemoveOptions.KeepNoTrivia);
            return removed is not null ? FuzzSyntaxHelpers.RenderFullNode(removed) : null;
        }

        var replacedRoot = ReplaceInvocationsWithTemplate(
            root, invocations, returnExprTemplate, paramNames, paramTypes);
        var helperInReplaced = FindDeviceFunctionByName(replacedRoot, helperName);
        if (helperInReplaced is not null &&
            !FuzzSyntaxHelpers.IsHelperReferencedOutsideItself(replacedRoot, helperInReplaced))
        {
            replacedRoot = replacedRoot.RemoveNode(helperInReplaced, SyntaxRemoveOptions.KeepNoTrivia) ?? replacedRoot;
        }

        return FuzzSyntaxHelpers.RenderFullNode(replacedRoot);
    }

    private static (string[] Names, string[] Types) ExtractParameters(MethodDeclarationSyntax helper)
    {
        int paramCount = helper.ParameterList.Parameters.Count;
        var paramNames = new string[paramCount];
        var paramTypes = new string[paramCount];
        for (int i = 0; i < paramCount; i++)
        {
            var p = helper.ParameterList.Parameters[i];
            paramNames[i] = p.Identifier.ValueText;
            paramTypes[i] = p.Type is not null ? FuzzSyntaxHelpers.RenderNodeText(p.Type) : "int";
        }

        return (paramNames, paramTypes);
    }

    private static List<InvocationExpressionSyntax> CollectMatchingInvocations(
        CompilationUnitSyntax root,
        MethodDeclarationSyntax helper,
        string helperName,
        int paramCount)
    {
        var invocations = new List<InvocationExpressionSyntax>();
        foreach (var node in root.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax inv &&
                !helper.Span.Contains(inv.Span) &&
                string.Equals(FuzzSyntaxHelpers.GetInvokedMethodName(inv), helperName, StringComparison.Ordinal) &&
                inv.ArgumentList.Arguments.Count == paramCount)
            {
                invocations.Add(inv);
            }
        }

        return invocations;
    }

    private static CompilationUnitSyntax ReplaceInvocationsWithTemplate(
        CompilationUnitSyntax root,
        List<InvocationExpressionSyntax> invocations,
        string returnExprTemplate,
        string[] paramNames,
        string[] paramTypes)
    {
        return root.ReplaceNodes(invocations, (origInv, _) =>
        {
            var argExprs = new string[origInv.ArgumentList.Arguments.Count];
            for (int i = 0; i < argExprs.Length; i++)
            {
                argExprs[i] = FuzzSyntaxHelpers.RenderNodeText(origInv.ArgumentList.Arguments[i].Expression);
            }

            string substituted = SubstituteParameters(returnExprTemplate, paramNames, paramTypes, argExprs);
            return SyntaxFactory.ParseExpression($"({substituted})").WithTriviaFrom(origInv);
        });
    }

    private static MethodDeclarationSyntax? FindDeviceFunctionByName(CompilationUnitSyntax root, string helperName)
    {
        foreach (var m in FuzzSyntaxHelpers.GetDeviceFunctionMethods(root))
        {
            if (string.Equals(m.Identifier.ValueText, helperName, StringComparison.Ordinal))
            {
                return m;
            }
        }

        return null;
    }

    private static bool HasByRefParameters(MethodDeclarationSyntax helper)
    {
        foreach (var p in helper.ParameterList.Parameters)
        {
            foreach (var m in p.Modifiers)
            {
                if (m.IsKind(SyntaxKind.RefKeyword) ||
                    m.IsKind(SyntaxKind.OutKeyword) ||
                    m.IsKind(SyntaxKind.InKeyword))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? ExtractSingleReturnExpression(MethodDeclarationSyntax helper)
    {
        if (helper.ExpressionBody is not null)
        {
            return FuzzSyntaxHelpers.RenderNodeText(helper.ExpressionBody.Expression);
        }

        if (helper.Body is null)
        {
            return null;
        }

        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var stmt in helper.Body.Statements)
        {
            if (stmt is LocalDeclarationStatementSyntax localDecl && localDecl.Declaration.Type is not null)
            {
                RecordLocalDeclarationInEnvironment(localDecl, env);
            }
            else if (stmt is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assign } &&
                     assign.Left is IdentifierNameSyntax idLeft)
            {
                RecordAssignmentInEnvironment(assign, idLeft.Identifier.ValueText, env);
            }
            else if (stmt is ReturnStatementSyntax { Expression: { } retExpr })
            {
                return SubstituteUsingMap(FuzzSyntaxHelpers.RenderNodeText(retExpr), env);
            }
            else
            {
                return null;
            }
        }

        return null;
    }

    private static void RecordLocalDeclarationInEnvironment(
        LocalDeclarationStatementSyntax localDecl,
        Dictionary<string, string> env)
    {
        string typeText = FuzzSyntaxHelpers.RenderNodeText(localDecl.Declaration.Type);
        foreach (var v in localDecl.Declaration.Variables)
        {
            string vName = v.Identifier.ValueText;
            string initText = v.Initializer is not null
                ? SubstituteUsingMap(FuzzSyntaxHelpers.RenderNodeText(v.Initializer.Value), env)
                : $"default({typeText})";
            env[vName] = $"(({typeText})({initText}))";
        }
    }

    private static void RecordAssignmentInEnvironment(
        AssignmentExpressionSyntax assign,
        string targetName,
        Dictionary<string, string> env)
    {
        string rhsText = SubstituteUsingMap(FuzzSyntaxHelpers.RenderNodeText(assign.Right), env);
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
        var ids = new List<IdentifierNameSyntax>();
        foreach (var node in expr.DescendantNodesAndSelf())
        {
            if (node is IdentifierNameSyntax id &&
                !FuzzSyntaxHelpers.IsMemberAccessRight(id) &&
                map.ContainsKey(id.Identifier.ValueText))
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            return exprText;
        }

        var replaced = expr.ReplaceNodes(ids, (orig, _) =>
            SyntaxFactory.ParseExpression(map[orig.Identifier.ValueText]).WithTriviaFrom(orig));
        return FuzzSyntaxHelpers.RenderFullNode(replaced);
    }

    private static string TrySimplifyStatementAt(string current, int stmtIdx, Func<string, bool> isInteresting)
    {
        var root = FuzzSyntaxHelpers.ParseRoot(current);
        var kernels = FuzzSyntaxHelpers.GetKernelMethods(root);
        if (kernels.Count == 0 || kernels[0].Body is null || stmtIdx >= kernels[0].Body!.Statements.Count)
        {
            return current;
        }

        var stmt = kernels[0].Body!.Statements[stmtIdx];
        if (FuzzSyntaxHelpers.IsChecksumFoldStatement(stmt))
        {
            return current;
        }

        var candidateExprs = new List<ExpressionSyntax>();
        foreach (var node in stmt.DescendantNodes())
        {
            if (node is BinaryExpressionSyntax or ConditionalExpressionSyntax)
            {
                candidateExprs.Add((ExpressionSyntax)node);
            }
        }

        foreach (var expr in candidateExprs)
        {
            IReadOnlyList<ExpressionSyntax> replacements = expr switch
            {
                ConditionalExpressionSyntax cond => [cond.WhenTrue, cond.WhenFalse],
                BinaryExpressionSyntax bin when !FuzzSyntaxHelpers.IsBooleanComparison(bin) => [bin.Left, bin.Right],
                _ => [],
            };

            foreach (var rep in replacements)
            {
                var newStmt = stmt.ReplaceNode(expr, SyntaxFactory.ParenthesizedExpression(rep.WithoutTrivia()));
                string candidate = FuzzSyntaxHelpers.RenderFullNode(root.ReplaceNode(stmt, newStmt));
                if (isInteresting(candidate))
                {
                    return candidate;
                }
            }
        }

        return current;
    }

    private static HashSet<int> FindUnusedHoistedLocalStatementIndices(SyntaxList<StatementSyntax> statements)
    {
        var unusedIndices = new HashSet<int>();
        for (int i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not LocalDeclarationStatementSyntax localDecl ||
                !FuzzSyntaxHelpers.IsHoistedDefaultDeclaration(localDecl) ||
                localDecl.Declaration.Variables.Count != 1)
            {
                continue;
            }

            string varName = localDecl.Declaration.Variables[0].Identifier.ValueText;
            bool usedElsewhere = false;
            for (int j = 0; j < statements.Count; j++)
            {
                if (j != i && StatementReferencesVariable(statements[j], varName))
                {
                    usedElsewhere = true;
                    break;
                }
            }

            if (!usedElsewhere)
            {
                unusedIndices.Add(i);
            }
        }

        return unusedIndices;
    }

    private static bool TryCollapseOneSingleAssignmentLocal(
        CompilationUnitSyntax root,
        MethodDeclarationSyntax method,
        Func<string, bool> isInteresting,
        [NotNullWhen(true)] out string? updatedSource)
    {
        var statements = method.Body!.Statements;
        for (int i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not ExpressionStatementSyntax
                {
                    Expression: AssignmentExpressionSyntax
                    {
                        RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                        Left: IdentifierNameSyntax leftId,
                    } assign,
                })
            {
                continue;
            }

            string varName = leftId.Identifier.ValueText;
            if (varName is "gid" or "hash" || NodeReferencesVariable(assign.Right, varName))
            {
                continue;
            }

            if (!TryFindSingleReaderReference(statements, i, varName, out int readerIdx, out var readerRef))
            {
                continue;
            }

            string candidate = BuildCollapsedCandidate(root, method, i, readerIdx, readerRef, assign.Right, varName);
            if (isInteresting(candidate))
            {
                updatedSource = candidate;
                return true;
            }
        }

        updatedSource = null;
        return false;
    }

    private static bool TryFindSingleReaderReference(
        SyntaxList<StatementSyntax> statements,
        int writerIdx,
        string varName,
        out int readerIdx,
        [NotNullWhen(true)] out IdentifierNameSyntax? singleRef)
    {
        singleRef = null;
        readerIdx = FindSingleReaderStatementIndex(statements, writerIdx, varName);
        if (readerIdx <= writerIdx)
        {
            return false;
        }

        var readerStmt = statements[readerIdx];
        var readerRefs = new List<IdentifierNameSyntax>();
        foreach (var node in readerStmt.DescendantNodes())
        {
            if (node is IdentifierNameSyntax id &&
                string.Equals(id.Identifier.ValueText, varName, StringComparison.Ordinal) &&
                !FuzzSyntaxHelpers.IsAssignedOrMutatedTarget(id))
            {
                readerRefs.Add(id);
            }
        }

        if (readerRefs.Count != 1)
        {
            return false;
        }

        singleRef = readerRefs[0];
        return true;
    }

    private static string BuildCollapsedCandidate(
        CompilationUnitSyntax root,
        MethodDeclarationSyntax method,
        int writerIdx,
        int readerIdx,
        IdentifierNameSyntax readerRef,
        ExpressionSyntax rhs,
        string varName)
    {
        var statements = method.Body!.Statements;
        string? declaredType = FuzzSyntaxHelpers.FindHoistedLocalType(statements, varName);
        string rhsText = FuzzSyntaxHelpers.RenderNodeText(rhs);
        string replacementExprText = declaredType is not null
            ? $"(({declaredType})({rhsText}))"
            : $"({rhsText})";

        var readerStmt = statements[readerIdx];
        var newReaderStmt = readerStmt.ReplaceNode(
            readerRef,
            SyntaxFactory.ParseExpression(replacementExprText).WithTriviaFrom(readerRef));

        var newStmts = new List<StatementSyntax>(statements.Count - 1);
        for (int k = 0; k < statements.Count; k++)
        {
            if (k != writerIdx)
            {
                newStmts.Add(k == readerIdx ? newReaderStmt : statements[k]);
            }
        }

        var newMethod = method.WithBody(method.Body!.WithStatements(SyntaxFactory.List(newStmts)));
        return FuzzSyntaxHelpers.RenderFullNode(root.ReplaceNode(method, newMethod));
    }

    private static int FindSingleReaderStatementIndex(
        SyntaxList<StatementSyntax> statements,
        int writerIdx,
        string varName)
    {
        int foundIdx = -1;
        for (int j = 0; j < statements.Count; j++)
        {
            if (j == writerIdx || FuzzSyntaxHelpers.IsHoistedDefaultDeclaration(statements[j]))
            {
                continue;
            }

            if (StatementReferencesVariable(statements[j], varName))
            {
                if (foundIdx != -1)
                {
                    return -1;
                }

                foundIdx = j;
            }
        }

        return foundIdx;
    }

    private static bool StatementReferencesVariable(StatementSyntax stmt, string varName) =>
        NodeReferencesVariable(stmt, varName);

    private static bool NodeReferencesVariable(SyntaxNode rootNode, string varName)
    {
        foreach (var node in rootNode.DescendantNodesAndSelf())
        {
            if (node is IdentifierNameSyntax id && string.Equals(id.Identifier.ValueText, varName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
