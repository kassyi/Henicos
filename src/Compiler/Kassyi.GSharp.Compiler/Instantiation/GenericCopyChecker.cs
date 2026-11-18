namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Checks that the closed copy of a generic declaration means what the declaration means for its instance: every
/// expression of the copy binds the same symbol, has the same type and conversion and the same constant as the node it
/// came from, read as the instance (<see cref="InstanceSymbolMap"/>). A copy that differs anywhere, or that does not
/// compile, is not emitted, so a rewrite the planner missed becomes a diagnostic instead of a different result. The
/// comparison of one node is <see cref="CopyNodeComparer"/>.
/// </summary>
internal static class GenericCopyChecker
{
    /// <summary>The nodes where the copy differs from the declaration.</summary>
    /// <param name="nodes">The nodes of the declaration, <see cref="CopyNodeIndex.List"/>.</param>
    /// <param name="declaration">The generic declaration, in the compilation of <paramref name="model"/>.</param>
    /// <param name="model">The semantic model of the declaration.</param>
    /// <param name="copy">The copy, in the derived compilation of <paramref name="copyModel"/>.</param>
    /// <param name="copyModel">The semantic model of the copy.</param>
    /// <param name="plan">The plan the copy was written from.</param>
    /// <param name="map">The type arguments of the instance.</param>
    public static ImmutableArray<CopyProblem> Check(
        SyntaxNode[] nodes,
        MethodDeclarationSyntax declaration,
        SemanticModel model,
        MethodDeclarationSyntax copy,
        SemanticModel copyModel,
        GenericCopyPlan plan,
        InstanceSymbolMap map)
    {
        var problems = ImmutableArray.CreateBuilder<CopyProblem>();
        AddCompileErrors(copy, copyModel, problems);

        var originalNodes = IndexOriginalNodes(nodes);
        var copyNodes = IndexCopyNodes(copy, nodes.Length);
        var context = new CopyCheckContext(model, copyModel, plan, map, new CopySymbolComparer(map, originalNodes),
            originalNodes);
        var replaced = CollectReplaced(nodes, plan);
        for (var i = 1; i < nodes.Length; i++)
        {
            var node = nodes[i];
            if (!IsCompared(node, i, replaced, declaration))
            {
                continue;
            }

            if (!copyNodes.TryGetValue(i, out var copied))
            {
                problems.Add(new CopyProblem(i, "the copy lost this node"));
            }
            else if (CopyNodeComparer.Compare(context, i, node, copied) is { } reason)
            {
                problems.Add(new CopyProblem(i, reason));
            }
        }

        return problems.ToImmutable();
    }

    private static Dictionary<SyntaxNode, int> IndexOriginalNodes(SyntaxNode[] nodes)
    {
        var originalNodes = new Dictionary<SyntaxNode, int>(nodes.Length);
        for (var i = 1; i < nodes.Length; i++)
        {
            originalNodes[nodes[i]] = i;
        }

        return originalNodes;
    }

    private static Dictionary<int, SyntaxNode> IndexCopyNodes(MethodDeclarationSyntax copy, int capacity)
    {
        var copyNodes = new Dictionary<int, SyntaxNode>(capacity);
        foreach (var node in copy.DescendantNodes())
        {
            if (CopyNodeIndex.TryGet(node, out var index) && !copyNodes.ContainsKey(index))
            {
                copyNodes[index] = node;
            }
        }

        return copyNodes;
    }

    // The nodes the copy has a counterpart for: an expression or pattern that the plan did not replace wholesale
    private static bool IsCompared(
        SyntaxNode node,
        int index,
        HashSet<int> replaced,
        MethodDeclarationSyntax declaration) =>
        node is (ExpressionSyntax or PatternSyntax) and
            not (CheckedExpressionSyntax or ParenthesizedExpressionSyntax) &&
        !replaced.Contains(index) &&
        GenericCopyPlanner.IsCopied(node, declaration);

    private static void AddCompileErrors(
        MethodDeclarationSyntax copy,
        SemanticModel copyModel,
        ImmutableArray<CopyProblem>.Builder problems)
    {
        foreach (var diagnostic in copyModel.GetDiagnostics(copy.Span))
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
            {
                continue;
            }

            var index = 0;
            var node = copy.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            foreach (var ancestor in node.AncestorsAndSelf())
            {
                if (CopyNodeIndex.TryGet(ancestor, out index))
                {
                    break;
                }
            }

            problems.Add(new CopyProblem(index, $"the copy does not compile: {diagnostic.Id}"));
        }
    }

    // The nodes that have no counterpart in the copy: inside a node written as a constant or as false, and the callee
    // of a call the copy makes through a helper, a closed form or a closed call of Math
    private static HashSet<int> CollectReplaced(SyntaxNode[] nodes, GenericCopyPlan plan)
    {
        var replaced = new HashSet<int>();
        foreach (var rewrite in plan.Rewrites.Values)
        {
            var root = nodes[rewrite.NodeIndex];
            var inside = rewrite switch
            {
                CopyRewrite.IdentityConstant or CopyRewrite.StaticMember or CopyRewrite.NameOf or
                    CopyRewrite.FalseCall => root,
                CopyRewrite.ConstrainedCall or CopyRewrite.ArgumentCall or CopyRewrite.MathCall =>
                    (root as InvocationExpressionSyntax)?.Expression,
                _ => null,
            };
            if (inside is null)
            {
                continue;
            }

            for (var i = rewrite.NodeIndex + 1; i < nodes.Length && root.Span.Contains(nodes[i].Span); i++)
            {
                if (inside.Span.Contains(nodes[i].Span))
                {
                    replaced.Add(i);
                }
            }
        }

        return replaced;
    }
}
