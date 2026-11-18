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
            originalNodes);
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

    // The rewrites that write the value of an operator in the type of the instance: the operands of C# are promoted
    private static bool IsNarrowing(CopyRewrite rewrite) =>
        rewrite is CopyRewrite.NarrowedOperator or CopyRewrite.NarrowedShift or CopyRewrite.TypedNegation or
            CopyRewrite.NarrowedNegation or CopyRewrite.NarrowedCompoundDivision or CopyRewrite.NarrowedCompoundShift;

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

    private sealed class PairCheck(
        SemanticModel model,
        SemanticModel copyModel,
        GenericCopyPlan plan,
        InstanceSymbolMap map,
        CopySymbolComparer comparer,
        Dictionary<SyntaxNode, int> originalNodes)
    {
        // The reason the copied node differs, or null
        public string? Compare(int index, SyntaxNode original, SyntaxNode copied)
        {
            var originalType = model.GetTypeInfo(original);
            var copyType = copyModel.GetTypeInfo(copied);
            var expectedType = Substitute(originalType.Type);
            if (!comparer.SameType(expectedType, copyType.Type))
            {
                return $"the copy has the type '{copyType.Type?.Name}' " +
                       $"where the declaration has '{originalType.Type?.Name}'";
            }

            var promoted = false;
            var expectedConverted = Substitute(originalType.ConvertedType);
            if (!comparer.SameType(expectedConverted, copyType.ConvertedType))
            {
                promoted = IsAllowedPromotion(original, copied, expectedType, expectedConverted,
                    copyType.ConvertedType);
                if (!promoted)
                {
                    return $"the copy converts to '{copyType.ConvertedType?.Name}' " +
                           $"where the declaration converts to '{originalType.ConvertedType?.Name}'";
                }
            }

            if (plan.Rewrites.TryGetValue(index, out var rewrite))
            {
                switch (rewrite)
                {
                    case CopyRewrite.TypeArgument or CopyRewrite.ExplicitTypeArguments or
                        CopyRewrite.StaticExtensionCall:
                        break;
                    case CopyRewrite.ConstrainedCall or CopyRewrite.ArgumentCall:
                        return promoted ? null : CompareConversions(original, copied);
                    case CopyRewrite.MathCall math:
                        return CompareMathCall(math, copied) ??
                               (promoted ? null : CompareConversions(original, copied));
                    default:
                        return CompareWritten(rewrite, original, copied);
                }
            }
            else if (IsMissingNarrowing(original))
            {
                return "the copy does not narrow the operator";
            }

            return CompareSymbols(original, copied) ?? (promoted ? null : CompareConversions(original, copied)) ??
                   CompareConstants(original, copied);
        }

        // [WHY] Every planned rewrite must be verified against the copy's syntax and bound symbols so an un-narrowed
        // compound assignment, wrong Math/static member, or wrong operator cannot pass just because the plan has it.
        private string? CompareMathCall(CopyRewrite.MathCall math, SyntaxNode copied)
        {
            var node = UnwrapNode(copied);
            if (math.ResultType is not null)
            {
                if (node is not CastExpressionSyntax cast)
                {
                    return "the copy does not cast the Math call";
                }

                node = UnwrapNode(cast.Expression);
            }

            if (node is not InvocationExpressionSyntax invocation ||
                copyModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { IsStatic: true } callee ||
                callee.Name != math.Name || !IsSystemMath(callee.ContainingType))
            {
                return $"the copy does not call 'System.Math.{math.Name}'";
            }

            return null;
        }

        // A node written as a constant, as false or as a narrowed operator: its value and form match what the plan says
        private string? CompareWritten(CopyRewrite rewrite, SyntaxNode original, SyntaxNode copied)
        {
            return rewrite switch
            {
                CopyRewrite.IdentityConstant identity when !IsIdentityConstant(identity, copied) =>
                    "the copy does not give the identity of the type",
                CopyRewrite.StaticMember member when !IsStaticMember(member, copied) =>
                    $"the copy does not read '{member.Type.Name}.{member.Name}'",
                CopyRewrite.NameOf nameOf when !Equals(copyModel.GetConstantValue(copied).Value, nameOf.Text) =>
                    "the copy does not give the name the declaration gives",
                CopyRewrite.FalseCall when !Equals(copyModel.GetConstantValue(copied).Value, false) =>
                    "the copy does not give false",
                CopyRewrite.NarrowedOperator narrowed when !IsNarrowedOperator(narrowed, original, copied) =>
                    "the copy does not narrow the operator",
                CopyRewrite.NarrowedShift shift when !IsNarrowedShift(shift, original, copied) =>
                    "the copy does not narrow the shift",
                CopyRewrite.TypedNegation negation when !IsTypedNegation(negation, copied) =>
                    "the copy does not negate in the type",
                CopyRewrite.NarrowedNegation negation when !IsNarrowedNegation(negation, copied) =>
                    "the copy does not narrow the negation",
                CopyRewrite.NarrowedCompoundDivision division
                    when !IsNarrowedCompoundDivision(division, copied) =>
                    "the copy does not narrow the operator",
                CopyRewrite.NarrowedCompoundShift shift when !IsNarrowedCompoundShift(shift, copied) =>
                    "the copy does not narrow the shift",
                _ => null,
            };
        }

        // [WHY] A constant identity matches its numeric value (0m == 0.0m in C# decimal); when marked opaque to
        // prevent constant folding of / 0 or checked overflow, it matches the emitter's ((T)Math.Max(c, c)) form.
        private bool IsIdentityConstant(CopyRewrite.IdentityConstant identity, SyntaxNode copied)
        {
            var constant = copyModel.GetConstantValue(copied);
            if (constant.HasValue)
            {
                return IsNumber(constant.Value, identity.Value);
            }

            return identity.Opaque && IsOpaqueMax(copied) is var (first, second) &&
                   IsTypedNumber(first, identity.Type, identity.Value) &&
                   IsTypedNumber(second, identity.Type, identity.Value);
        }

        private bool IsStaticMember(CopyRewrite.StaticMember member, SyntaxNode copied)
        {
            if (member.Opaque)
            {
                return IsOpaqueMax(copied) is var (first, second) &&
                       IsStaticMemberCast(first, member.Type, member.Name) &&
                       IsStaticMemberCast(second, member.Type, member.Name);
            }

            return IsStaticMemberCast(copied, member.Type, member.Name);
        }

        private bool IsStaticMemberCast(SyntaxNode node, ITypeSymbol type, string name)
        {
            if (UnwrapNode(node) is not CastExpressionSyntax cast ||
                !comparer.SameType(type, copyModel.GetTypeInfo(cast).Type))
            {
                return false;
            }

            return UnwrapNode(cast.Expression) is MemberAccessExpressionSyntax access &&
                   copyModel.GetSymbolInfo(access).Symbol is { IsStatic: true } symbol &&
                   symbol.DeclaredAccessibility == Accessibility.Public &&
                   symbol.Name == name &&
                   comparer.SameType(type, symbol.ContainingType);
        }

        private (ExpressionSyntax First, ExpressionSyntax Second)? IsOpaqueMax(SyntaxNode copied)
        {
            if (UnwrapNode(copied) is not CastExpressionSyntax cast ||
                UnwrapNode(cast.Expression) is not InvocationExpressionSyntax invocation ||
                invocation.ArgumentList.Arguments is not [var first, var second] ||
                !first.RefKindKeyword.IsKind(SyntaxKind.None) ||
                !second.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                return null;
            }

            if (copyModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "Max", IsStatic: true } max ||
                !IsSystemMath(max.ContainingType))
            {
                return null;
            }

            return (first.Expression, second.Expression);
        }

        private bool IsSystemMath(ITypeSymbol? type)
        {
            var mathType = copyModel.Compilation.GetTypeByMetadataName("System.Math");
            return mathType is not null && SymbolEqualityComparer.Default.Equals(mathType, type);
        }

        private bool IsTypedNumber(ExpressionSyntax expression, ITypeSymbol expectedType, int expectedValue)
        {
            var constant = copyModel.GetConstantValue(expression);
            return constant.HasValue && IsNumber(constant.Value, expectedValue) &&
                   comparer.SameType(expectedType, copyModel.GetTypeInfo(expression).Type);
        }

        private bool IsNarrowedOperator(
            CopyRewrite.NarrowedOperator narrowed,
            SyntaxNode original,
            SyntaxNode copied) =>
            UnwrapNarrowing(copied, narrowed.Type, narrowed.Narrowing) is { } inner &&
            inner.Kind() == original.Kind() &&
            CompareSymbols(original, inner) is null;

        private bool IsNarrowedShift(CopyRewrite.NarrowedShift shift, SyntaxNode original, SyntaxNode copied) =>
            UnwrapNarrowing(copied, shift.Type, shift.Narrowing) is { } inner &&
            IsMaskedShift(inner, original.Kind(), shift.CountMask, shift.LeftMask);

        private bool IsTypedNegation(CopyRewrite.TypedNegation negation, SyntaxNode copied) =>
            UnwrapNode(copied) is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.SubtractExpression } sub &&
            IsZeroConstantOrCast(sub.Left) &&
            comparer.SameType(negation.Type, copyModel.GetTypeInfo(sub).Type);

        private bool IsNarrowedNegation(CopyRewrite.NarrowedNegation negation, SyntaxNode copied) =>
            UnwrapNarrowing(copied, negation.Type, negation.Narrowing) is
                BinaryExpressionSyntax { RawKind: (int)SyntaxKind.SubtractExpression } sub &&
            IsZeroConstantOrCast(sub.Left);

        private bool IsNarrowedCompoundDivision(CopyRewrite.NarrowedCompoundDivision division, SyntaxNode copied) =>
            UnwrapNode(copied) is AssignmentExpressionSyntax
            {
                RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
            } assignment &&
            UnwrapNarrowing(assignment.Right, division.Type, division.Narrowing) is
                BinaryExpressionSyntax { RawKind: (int)SyntaxKind.DivideExpression };

        private bool IsNarrowedCompoundShift(CopyRewrite.NarrowedCompoundShift shift, SyntaxNode copied) =>
            UnwrapNode(copied) is AssignmentExpressionSyntax
            {
                RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
            } assignment &&
            UnwrapNarrowing(assignment.Right, shift.Type, shift.Narrowing) is { } inner &&
            IsMaskedShift(inner, shift.Operator, shift.CountMask, shift.LeftMask);

        private ExpressionSyntax? UnwrapNarrowing(SyntaxNode node, ITypeSymbol type, Narrowing narrowing)
        {
            if (UnwrapNode(node) is not CastExpressionSyntax cast ||
                !comparer.SameType(type, copyModel.GetTypeInfo(cast).Type))
            {
                return null;
            }

            var inRange = UnwrapNode(cast.Expression);
            return narrowing switch
            {
                Narrowing.Cast => inRange as ExpressionSyntax,
                Narrowing.LowBits lowBits => UnwrapBitwiseMask(inRange, lowBits.Mask),
                Narrowing.SignExtended extended => UnwrapSignExtension(inRange, extended.Shift),
                _ => null,
            };
        }

        private ExpressionSyntax? UnwrapSignExtension(SyntaxNode node, int shift) =>
            node is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.RightShiftExpression } rightShift &&
            copyModel.GetConstantValue(rightShift.Right) is { HasValue: true } rightConst &&
            IsNumber(rightConst.Value, shift) &&
            UnwrapNode(rightShift.Left) is
                BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LeftShiftExpression } leftShift &&
            copyModel.GetConstantValue(leftShift.Right) is { HasValue: true } leftConst &&
            IsNumber(leftConst.Value, shift)
                ? UnwrapNode(leftShift.Left) as ExpressionSyntax
                : null;

        private ExpressionSyntax? UnwrapBitwiseMask(SyntaxNode node, int mask) =>
            node is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.BitwiseAndExpression } andExpr &&
            copyModel.GetConstantValue(andExpr.Right) is { HasValue: true } constant &&
            IsNumber(constant.Value, mask)
                ? UnwrapNode(andExpr.Left) as ExpressionSyntax
                : null;

        private bool IsMaskedShift(SyntaxNode node, SyntaxKind operatorKind, int countMask, int? leftMask) =>
            node is BinaryExpressionSyntax binary &&
            binary.Kind() == operatorKind &&
            UnwrapBitwiseMask(UnwrapNode(binary.Right), countMask) is not null &&
            (leftMask is not { } mask || UnwrapBitwiseMask(UnwrapNode(binary.Left), mask) is not null);

        private bool IsZeroConstantOrCast(ExpressionSyntax expression)
        {
            var constant = copyModel.GetConstantValue(expression);
            if (constant.HasValue)
            {
                return IsNumber(constant.Value, 0);
            }

            return UnwrapNode(expression) is CastExpressionSyntax cast &&
                   copyModel.GetConstantValue(cast.Expression) is { HasValue: true } inner &&
                   IsNumber(inner.Value, 0);
        }

        // [WHY] C# 12.4.7 promotes sbyte, byte, short, ushort and char to int on built-in operators, and System.Math
        // has no char overload (widening char to ushort for Min/Max, or int via numeric promotion for Sign).
        private bool IsAllowedPromotion(
            SyntaxNode original,
            SyntaxNode copied,
            ITypeSymbol? expectedType,
            ITypeSymbol? expectedConverted,
            ITypeSymbol? copyConverted)
        {
            if (expectedType is null || copyConverted is null ||
                !comparer.SameType(expectedType, expectedConverted) ||
                copied is not ExpressionSyntax copiedExpr ||
                copyModel.GetConversion(copiedExpr) is not
                    { Exists: true, IsImplicit: true, IsNumeric: true, MethodSymbol: null })
            {
                return false;
            }

            if (IsMathCallCharPromotion(original, expectedType, copyConverted))
            {
                return true;
            }

            var promoted = BuiltinNumerics.GetPromotedSpecialType(expectedType);
            if (promoted == SpecialType.None || copyConverted.SpecialType != promoted)
            {
                return false;
            }

            var (operand, parent) = UnwrapParentWithOperand(original);
            return parent is not null &&
                   (HasNarrowingRewrite(parent) || IsPromotedUnnarrowedOperand(operand, parent, expectedType));
        }

        private bool IsMathCallCharPromotion(
            SyntaxNode original,
            ITypeSymbol expectedType,
            ITypeSymbol copyConverted)
        {
            if (expectedType.SpecialType != SpecialType.System_Char ||
                UnwrapParentWithOperand(original).Parent is not ArgumentSyntax
                {
                    Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call },
                } ||
                !originalNodes.TryGetValue(call, out var callIndex) ||
                !plan.Rewrites.TryGetValue(callIndex, out var callRewrite) ||
                callRewrite is not CopyRewrite.MathCall math)
            {
                return false;
            }

            return (math.Name is "Max" or "Min" && copyConverted.SpecialType == SpecialType.System_UInt16) ||
                   (math.Name == "Sign" &&
                    copyConverted.SpecialType == BuiltinNumerics.GetPromotedSpecialType(expectedType));
        }

        private bool HasNarrowingRewrite(SyntaxNode node) =>
            originalNodes.TryGetValue(node, out var nodeIndex) &&
            plan.Rewrites.TryGetValue(nodeIndex, out var nodeRewrite) &&
            IsNarrowing(nodeRewrite);

        private bool IsPromotedUnnarrowedOperand(SyntaxNode operand, SyntaxNode parent, ITypeSymbol expectedType)
        {
            var isUnnarrowedNode = parent switch
            {
                BinaryExpressionSyntax binary => binary.Kind() is SyntaxKind.EqualsExpression or
                    SyntaxKind.NotEqualsExpression or SyntaxKind.LessThanExpression or
                    SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanExpression or
                    SyntaxKind.GreaterThanOrEqualExpression,
                AssignmentExpressionSyntax assignment =>
                    ReferenceEquals(UnwrapNode(assignment.Right), UnwrapNode(operand)) &&
                    assignment.Kind() is SyntaxKind.AddAssignmentExpression or
                        SyntaxKind.SubtractAssignmentExpression or SyntaxKind.MultiplyAssignmentExpression or
                        SyntaxKind.ModuloAssignmentExpression or SyntaxKind.AndAssignmentExpression or
                        SyntaxKind.OrAssignmentExpression or SyntaxKind.ExclusiveOrAssignmentExpression,
                _ => false,
            };
            return isUnnarrowedNode && IsPromotedBuiltinOperator(parent, expectedType);
        }

        private bool IsMissingNarrowing(SyntaxNode node) =>
            node is AssignmentExpressionSyntax assignment &&
            assignment.Kind() is SyntaxKind.DivideAssignmentExpression or SyntaxKind.LeftShiftAssignmentExpression or
                SyntaxKind.RightShiftAssignmentExpression or SyntaxKind.UnsignedRightShiftAssignmentExpression &&
            model.GetTypeInfo(assignment).Type is { } type &&
            map.Substitute(type) is { } closed &&
            BuiltinNumerics.IsPromoted(closed) &&
            IsPromotedBuiltinOperator(assignment, closed);

        private bool IsPromotedBuiltinOperator(SyntaxNode node, ITypeSymbol closed) =>
            model.GetSymbolInfo(node).Symbol is { } symbol &&
            ConstrainedAccess.IsConstrainable(symbol) &&
            ConstrainedAccess.Find(node, model, symbol) is ITypeParameterSymbol parameter &&
            map.IsMapped(parameter) &&
            map.ResolveConstrained(symbol, parameter) is ConstrainedMember.BuiltinOperator builtin &&
            comparer.SameType(builtin.ClosedType, closed);

        private string? CompareSymbols(SyntaxNode original, SyntaxNode copied)
        {
            var symbol = model.GetSymbolInfo(original).Symbol;
            var copySymbol = copyModel.GetSymbolInfo(copied).Symbol;
            // [WHY] Interface instance members accessed through a constrained type parameter resolve to the closed
            // type's exact interface implementation (FindImplementationForInterfaceMember), just like static members.
            if (symbol is not null &&
                (ConstrainedAccess.IsConstrainable(symbol) || ConstrainedAccess.IsInterfaceInstanceMember(symbol)) &&
                ConstrainedAccess.Find(original, model, symbol) is ITypeParameterSymbol parameter &&
                map.IsMapped(parameter))
            {
                var resolved = map.ResolveConstrained(symbol, parameter);
                var same = resolved switch
                {
                    ConstrainedMember.Member member => comparer.Same(member.Implementation, copySymbol),
                    ConstrainedMember.BuiltinOperator builtin => copySymbol is IMethodSymbol
                                                                 {
                                                                     MethodKind: MethodKind.BuiltinOperator,
                                                                 } op &&
                                                                 SameOperator(op.Name, builtin.OperatorName),
                    _ => false,
                };
                return same
                    ? null
                    : (resolved as ConstrainedMember.Unsupported)?.Reason ??
                      Differs(copySymbol, (resolved as ConstrainedMember.Member)?.Implementation ?? symbol);
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
            expectedName is not null && CopySymbolComparer.SameOperatorName(copyName, expectedName);

        // [WHY] CopyRewrite.ArgumentCall (e.g. T.Abs(x) -> (x)) removes the call boundary, so promotion from an
        // enclosing operator or MathCall flows through the ArgumentCall directly to its argument.
        private (SyntaxNode Operand, SyntaxNode? Parent) UnwrapParentWithOperand(SyntaxNode node)
        {
            var operand = node;
            while (true)
            {
                var parent = operand.Parent;
                if (parent is ParenthesizedExpressionSyntax or CheckedExpressionSyntax)
                {
                    operand = parent;
                    continue;
                }

                if (parent is ArgumentSyntax argument &&
                    argument.Parent is ArgumentListSyntax { Parent: InvocationExpressionSyntax call } list &&
                    list.Arguments.Count == 1 &&
                    originalNodes.TryGetValue(call, out var callIndex) &&
                    plan.Rewrites.TryGetValue(callIndex, out var rewrite) &&
                    rewrite is CopyRewrite.ArgumentCall)
                {
                    operand = call;
                    continue;
                }

                return (operand, parent);
            }
        }

        private static SyntaxNode UnwrapNode(SyntaxNode node)
        {
            while (node is ParenthesizedExpressionSyntax or CheckedExpressionSyntax)
            {
                node = node is ParenthesizedExpressionSyntax parenthesized
                    ? parenthesized.Expression
                    : ((CheckedExpressionSyntax)node).Expression;
            }

            return node;
        }

        private static bool IsNumber(object? value, int expected) =>
            value switch
            {
                int i => i == expected,
                uint u => expected >= 0 && u == (uint)expected,
                long l => l == expected,
                ulong ul => expected >= 0 && ul == (ulong)expected,
                short s => s == expected,
                ushort us => expected >= 0 && us == (ushort)expected,
                sbyte sb => sb == expected,
                byte b => expected >= 0 && b == (byte)expected,
                char c => expected >= 0 && c == (char)expected,
                float f => SameBits(f, expected),
                double d => SameBits(d, expected),
                decimal m => m == expected,
                _ => false,
            };

        // [WHY] -0.0 == 0 in C#, but T.Zero is +0.0 and the two differ for 1 / x and Math.CopySign, so a floating-point
        // identity matches by its bits (float to double is exact, so one comparison serves both)
        private static bool SameBits(double value, int expected) =>
            BitConverter.DoubleToInt64Bits(value) == BitConverter.DoubleToInt64Bits(expected);

        private static string Differs(ISymbol? copy, ISymbol? expected) =>
            $"the copy binds '{Describe(copy)}' where the declaration binds '{Describe(expected)}'";

        private static string Describe(ISymbol? symbol) =>
            symbol is null ? "nothing"
            : symbol.ContainingType is { } type ? type.Name + "." + symbol.Name
            : symbol.Name;
    }
}
