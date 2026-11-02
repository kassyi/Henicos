using System.Linq;
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
            var method = FuzzSyntaxHelpers.GetKernelMethods(root).FirstOrDefault();
            if (method?.Body is null)
            {
                break;
            }

            var removableIndices = FuzzSyntaxHelpers.GetRemovableStatementIndices(method.Body.Statements);
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
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        if (methods.Count == 0)
        {
            return source;
        }

        string current = source;
        foreach (var m in methods)
        {
            string methodName = m.Identifier.ValueText;
            var latestRoot = FuzzSyntaxHelpers.ParseRoot(current);
            var method = latestRoot.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(x => string.Equals(x.Identifier.ValueText, methodName, StringComparison.Ordinal));
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
            var method = FuzzSyntaxHelpers.GetKernelMethods(root).FirstOrDefault();
            if (method?.Body is null)
            {
                break;
            }

            if (!TryCollapseOneSingleAssignmentLocal(root, method, isInteresting, out string? updated))
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

            string candidate = withoutHelper.ToFullString();
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
        var helper = FuzzSyntaxHelpers.GetDeviceFunctionMethods(root)
            .FirstOrDefault(m => string.Equals(m.Identifier.ValueText, helperName, StringComparison.Ordinal));
        if (helper is null || helper.TypeParameterList is not null || HasByRefParameters(helper))
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

        var invocations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => !helper.Span.Contains(inv.Span) &&
                          FuzzSyntaxHelpers.GetInvokedMethodName(inv) == helperName &&
                          inv.ArgumentList.Arguments.Count == paramNames.Length)
            .ToList();

        if (invocations.Count == 0)
        {
            return root.RemoveNode(helper, SyntaxRemoveOptions.KeepNoTrivia)?.ToFullString();
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

        var helperInReplaced = FuzzSyntaxHelpers.GetDeviceFunctionMethods(replacedRoot)
            .FirstOrDefault(m => string.Equals(m.Identifier.ValueText, helperName, StringComparison.Ordinal));
        if (helperInReplaced is not null && !FuzzSyntaxHelpers.IsHelperReferencedOutsideItself(replacedRoot, helperInReplaced))
        {
            replacedRoot = replacedRoot.RemoveNode(helperInReplaced, SyntaxRemoveOptions.KeepNoTrivia) ?? replacedRoot;
        }

        return replacedRoot.ToFullString();
    }

    private static bool HasByRefParameters(MethodDeclarationSyntax helper) =>
        helper.ParameterList.Parameters.Any(static p =>
            p.Modifiers.Any(static m =>
                m.IsKind(SyntaxKind.RefKeyword) ||
                m.IsKind(SyntaxKind.OutKeyword) ||
                m.IsKind(SyntaxKind.InKeyword)));

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
                return SubstituteUsingMap(retExpr.ToString(), env);
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

    private static void RecordAssignmentInEnvironment(
        AssignmentExpressionSyntax assign,
        string targetName,
        Dictionary<string, string> env)
    {
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
            .Where(id => !FuzzSyntaxHelpers.IsMemberAccessRight(id) && map.ContainsKey(id.Identifier.ValueText))
            .ToList();

        if (ids.Count == 0)
        {
            return exprText;
        }

        var replaced = expr.ReplaceNodes(ids, (orig, _) =>
            SyntaxFactory.ParseExpression(map[orig.Identifier.ValueText]).WithTriviaFrom(orig));
        return replaced.ToFullString();
    }

    private static string TrySimplifyStatementAt(string current, int stmtIdx, Func<string, bool> isInteresting)
    {
        var root = FuzzSyntaxHelpers.ParseRoot(current);
        var method = FuzzSyntaxHelpers.GetKernelMethods(root).FirstOrDefault();
        if (method?.Body is null || stmtIdx >= method.Body.Statements.Count)
        {
            return current;
        }

        var stmt = method.Body.Statements[stmtIdx];
        if (FuzzSyntaxHelpers.IsChecksumFoldStatement(stmt))
        {
            return current;
        }

        var candidateExprs = stmt.DescendantNodes()
            .OfType<ExpressionSyntax>()
            .Where(static e => e is BinaryExpressionSyntax or ConditionalExpressionSyntax)
            .ToList();

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
                string candidate = root.ReplaceNode(stmt, newStmt).ToFullString();
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
                if (j != i &&
                    statements[j].DescendantNodes().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.ValueText == varName))
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
            if (varName is "gid" or "hash" ||
                assign.Right.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.ValueText == varName))
            {
                continue;
            }

            int readerIdx = FindSingleReaderStatementIndex(statements, i, varName);
            if (readerIdx <= i)
            {
                continue;
            }

            var readerStmt = statements[readerIdx];
            var readerRefs = readerStmt.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(id => id.Identifier.ValueText == varName && !FuzzSyntaxHelpers.IsAssignedOrMutatedTarget(id))
                .ToList();

            if (readerRefs.Count != 1)
            {
                continue;
            }

            string? declaredType = FuzzSyntaxHelpers.FindHoistedLocalType(statements, varName);
            string replacementExprText = declaredType is not null
                ? $"(({declaredType})({assign.Right}))"
                : $"({assign.Right})";
            var newReaderStmt = readerStmt.ReplaceNode(
                readerRefs[0],
                SyntaxFactory.ParseExpression(replacementExprText).WithTriviaFrom(readerRefs[0]));

            var newStmts = new List<StatementSyntax>(statements.Count - 1);
            for (int k = 0; k < statements.Count; k++)
            {
                if (k != i)
                {
                    newStmts.Add(k == readerIdx ? newReaderStmt : statements[k]);
                }
            }

            var newMethod = method.WithBody(method.Body.WithStatements(SyntaxFactory.List(newStmts)));
            string candidate = root.ReplaceNode(method, newMethod).ToFullString();
            if (isInteresting(candidate))
            {
                updatedSource = candidate;
                return true;
            }
        }

        updatedSource = null;
        return false;
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

            if (statements[j].DescendantNodes().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.ValueText == varName))
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
}

