namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Checks that the closed copy of a generic declaration means what the declaration means for its instance: every
/// expression of the copy binds the same symbol, has the same type and conversion and the same constant as the node it
/// came from, read as the instance (<see cref="InstanceSymbolMap"/>). A copy that differs anywhere, or that does not
/// compile, is not emitted, so a rewrite the planner missed becomes a diagnostic instead of a different result.
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

        var originalNodes = new Dictionary<SyntaxNode, int>(nodes.Length);
        for (var i = 1; i < nodes.Length; i++)
        {
            originalNodes[nodes[i]] = i;
        }

        var copyNodes = new Dictionary<int, SyntaxNode>(nodes.Length);
        foreach (var node in copy.DescendantNodes())
        {
            if (CopyNodeIndex.TryGet(node, out var index) && !copyNodes.ContainsKey(index))
            {
                copyNodes[index] = node;
            }
        }

        var pair = new PairCheck(model, copyModel, plan, map, new CopySymbolComparer(map, originalNodes),
            CollectOperands(nodes, plan));
        var replaced = CollectReplaced(nodes, plan);
        for (var i = 1; i < nodes.Length; i++)
        {
            var node = nodes[i];
            if (node is not (ExpressionSyntax or PatternSyntax) || replaced.Contains(i) ||
                node is CheckedExpressionSyntax or ParenthesizedExpressionSyntax ||
                !GenericCopyPlanner.IsCopied(node, declaration))
            {
                continue;
            }

            if (!copyNodes.TryGetValue(i, out var copied))
            {
                problems.Add(new CopyProblem(i, "the copy lost this node"));
            }
            else if (pair.Compare(i, node, copied) is { } reason)
            {
                problems.Add(new CopyProblem(i, reason));
            }
        }

        return problems.ToImmutable();
    }

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

    // The direct operands of the operators the copy narrows: C# promotes a small operand to int there, where the
    // generic math operator takes the type itself, so only the type of an operand is compared, not its conversion
    private static HashSet<int> CollectOperands(SyntaxNode[] nodes, GenericCopyPlan plan)
    {
        var operands = new HashSet<int>();
        foreach (var rewrite in plan.Rewrites.Values)
        {
            if (rewrite.Kind is not (CopyRewriteKind.NarrowedOperator or CopyRewriteKind.NarrowedShift or
                CopyRewriteKind.NarrowedNegation))
            {
                continue;
            }

            var root = nodes[rewrite.NodeIndex];
            for (var i = rewrite.NodeIndex + 1; i < nodes.Length && root.Span.Contains(nodes[i].Span); i++)
            {
                if (nodes[i].Parent == root || nodes[i].Parent is ParenthesizedExpressionSyntax { Parent: var outer } &&
                    outer == root)
                {
                    operands.Add(i);
                }
            }
        }

        return operands;
    }

    // The nodes inside a node the copy writes as a constant: they have no counterpart
    private static HashSet<int> CollectReplaced(SyntaxNode[] nodes, GenericCopyPlan plan)
    {
        var replaced = new HashSet<int>();
        foreach (var rewrite in plan.Rewrites.Values)
        {
            if (rewrite.Kind is not (CopyRewriteKind.IdentityConstant or CopyRewriteKind.NameOfConstant))
            {
                continue;
            }

            var root = nodes[rewrite.NodeIndex];
            for (var i = rewrite.NodeIndex + 1; i < nodes.Length && root.Span.Contains(nodes[i].Span); i++)
            {
                replaced.Add(i);
            }
        }

        return replaced;
    }

    private sealed class PairCheck(
        SemanticModel model,
        SemanticModel copyModel,
        GenericCopyPlan plan,
        InstanceSymbolMap map,
        CopySymbolComparer comparer,
        HashSet<int> operands)
    {
        // The reason the copied node differs, or null
        public string? Compare(int index, SyntaxNode original, SyntaxNode copied)
        {
            var originalType = model.GetTypeInfo(original);
            var copyType = copyModel.GetTypeInfo(copied);
            var isOperand = operands.Contains(index);
            if (!comparer.SameType(Substitute(originalType.Type), copyType.Type) ||
                (!isOperand && !comparer.SameType(Substitute(originalType.ConvertedType), copyType.ConvertedType)))
            {
                return $"the copy has the type '{copyType.Type?.Name}' " +
                       $"where the declaration has '{originalType.Type?.Name}'";
            }

            if (plan.Rewrites.TryGetValue(index, out var rewrite) &&
                rewrite.Kind is CopyRewriteKind.IdentityConstant or CopyRewriteKind.NameOfConstant or
                    CopyRewriteKind.NarrowedOperator or CopyRewriteKind.NarrowedShift or
                    CopyRewriteKind.NarrowedNegation)
            {
                return CompareWritten(rewrite, copied);
            }

            return CompareSymbols(original, copied) ?? (isOperand ? null : CompareConversions(original, copied)) ??
                   CompareConstants(original, copied);
        }

        // A node written as a constant or a narrowed operator: its value is what the plan says
        private string? CompareWritten(CopyRewrite rewrite, SyntaxNode copied)
        {
            var constant = copyModel.GetConstantValue(copied);
            return rewrite.Kind switch
            {
                CopyRewriteKind.IdentityConstant when !constant.HasValue ||
                                                     !IsNumber(constant.Value, ConstantOf(rewrite)) =>
                    "the copy does not give the identity of the type",
                CopyRewriteKind.NameOfConstant when !Equals(constant.Value, rewrite.Constant) =>
                    "the copy does not give the name the declaration gives",
                _ => null,
            };
        }

        private string? CompareSymbols(SyntaxNode original, SyntaxNode copied)
        {
            var symbol = model.GetSymbolInfo(original).Symbol;
            var copySymbol = copyModel.GetSymbolInfo(copied).Symbol;
            if (ConstrainedAccess.IsConstrainable(symbol) && symbol is not null &&
                ConstrainedAccess.Find(original, model, symbol) is ITypeParameterSymbol parameter &&
                map.IsMapped(parameter))
            {
                var resolved = map.ResolveConstrained(symbol, parameter);
                var same = resolved.Kind switch
                {
                    ConstrainedMemberKind.Member => comparer.Same(resolved.Implementation, copySymbol),
                    ConstrainedMemberKind.BuiltinOperator => copySymbol is IMethodSymbol
                                                             {
                                                                 MethodKind: MethodKind.BuiltinOperator,
                                                             } op &&
                                                             SameOperator(op.Name, resolved.OperatorName),
                    _ => false,
                };
                return same ? null : resolved.Reason ?? Differs(copySymbol, resolved.Implementation ?? symbol);
            }

            var expected = map.Substitute(symbol);
            return comparer.Same(expected, copySymbol) ? null : Differs(copySymbol, expected);
        }

        private string? CompareConversions(SyntaxNode original, SyntaxNode copied)
        {
            if (original is not ExpressionSyntax expression || copied is not ExpressionSyntax copiedExpression)
            {
                return null;
            }

            var conversion = model.GetConversion(expression);
            var copyConversion = copyModel.GetConversion(copiedExpression);
            var same = conversion.Exists == copyConversion.Exists &&
                       conversion.IsImplicit == copyConversion.IsImplicit &&
                       conversion.IsNumeric == copyConversion.IsNumeric &&
                       conversion.IsNullable == copyConversion.IsNullable &&
                       conversion.IsUserDefined == copyConversion.IsUserDefined &&
                       comparer.Same(map.Substitute(conversion.MethodSymbol), copyConversion.MethodSymbol);
            return same ? null : "the copy converts the value another way";
        }

        private string? CompareConstants(SyntaxNode original, SyntaxNode copied)
        {
            var constant = model.GetConstantValue(original);
            return !constant.HasValue || Equals(constant.Value, copyModel.GetConstantValue(copied).Value)
                ? null
                : "the copy has another constant value";
        }

        private ITypeSymbol? Substitute(ITypeSymbol? type) => type is null ? null : map.Substitute(type);

        // The checked and unchecked operator of the same C# operator: the copy is in the same checked context
        private static bool SameOperator(string copyName, string? expectedName) =>
            expectedName is not null &&
            string.Equals(Unchecked(copyName), Unchecked(expectedName), StringComparison.Ordinal);

        private static int ConstantOf(CopyRewrite rewrite) => rewrite.Constant is int value ? value : -1;

        private static string Unchecked(string name) =>
            name.StartsWith("op_Checked", StringComparison.Ordinal) ? "op_" + name["op_Checked".Length..] : name;

        private static bool IsNumber(object? value, int expected) =>
            value switch
            {
                int i => i == expected,
                uint u => u == expected,
                long l => l == expected,
                ulong ul => ul == (ulong)expected,
                short s => s == expected,
                ushort us => us == expected,
                sbyte sb => sb == expected,
                byte b => b == expected,
                char c => c == expected,
                float f => f.Equals(expected),
                double d => d.Equals(expected),
                _ => false,
            };

        private static string Differs(ISymbol? copy, ISymbol? expected) =>
            $"the copy binds '{Describe(copy)}' where the declaration binds '{Describe(expected)}'";

        private static string Describe(ISymbol? symbol) =>
            symbol is null ? "nothing"
            : symbol.ContainingType is { } type ? type.Name + "." + symbol.Name
            : symbol.Name;
    }
}
