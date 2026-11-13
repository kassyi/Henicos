namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Decides how the closed copy of a generic declaration is written: which names become the type arguments, and which
/// calls and operators are pinned to what the declaration binds, so that the copy does not bind to something else once
/// the type parameters are replaced (<see cref="CopyRewriteKind"/>). Nothing is written here
/// (<see cref="GenericCopyEmitter"/> writes the copy); <see cref="GenericCopyChecker"/> checks the result.
/// </summary>
internal static class GenericCopyPlanner
{
    /// <summary>Plans the copy of <paramref name="declaration"/> for the instance <paramref name="map"/> reads.</summary>
    /// <param name="declaration">The generic declaration, in the compilation of <paramref name="model"/>.</param>
    /// <param name="nodes">The nodes of the declaration, <see cref="CopyNodeIndex.List"/>.</param>
    /// <param name="model">The semantic model of the declaration.</param>
    /// <param name="map">The type arguments of the instance.</param>
    /// <param name="instance">The instance the copy is for.</param>
    public static GenericCopyPlan Plan(
        MethodDeclarationSyntax declaration,
        SyntaxNode[] nodes,
        SemanticModel model,
        InstanceSymbolMap map,
        GenericInstance instance)
    {
        var plan = new Builder(model, map);
        for (var i = 0; i < nodes.Length; i++)
        {
            if (IsCopied(nodes[i], declaration))
            {
                plan.Visit(i, nodes[i]);
            }
        }

        return new GenericCopyPlan(instance, plan.Rewrites.ToImmutable(), plan.Problems.ToImmutable());
    }

    /// <summary>
    /// Whether <paramref name="node"/> is part of what the copy writes from the declaration: its return type,
    /// parameters and body, but not its attributes, type parameters and constraints.
    /// </summary>
    public static bool IsCopied(SyntaxNode node, MethodDeclarationSyntax declaration) =>
        node != declaration && node.FirstAncestorOrSelf<AttributeListSyntax>() is null &&
        (declaration.ReturnType.Span.Contains(node.Span) || declaration.ParameterList.Span.Contains(node.Span) ||
         (declaration.Body?.Span.Contains(node.Span) ?? false) ||
         (declaration.ExpressionBody?.Span.Contains(node.Span) ?? false));

    private sealed class Builder(SemanticModel model, InstanceSymbolMap map)
    {
        public ImmutableDictionary<int, CopyRewrite>.Builder Rewrites { get; } =
            ImmutableDictionary.CreateBuilder<int, CopyRewrite>();

        public ImmutableArray<CopyProblem>.Builder Problems { get; } = ImmutableArray.CreateBuilder<CopyProblem>();

        public void Visit(int index, SyntaxNode node)
        {
            switch (node)
            {
                case IdentifierNameSyntax name:
                    VisitName(index, name);
                    break;
                case InvocationExpressionSyntax invocation:
                    VisitInvocation(index, invocation);
                    break;
                case MemberAccessExpressionSyntax access:
                    VisitConstrainedMember(index, access);
                    break;
                case BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or
                    AssignmentExpressionSyntax:
                    VisitOperator(index, (ExpressionSyntax)node);
                    break;
            }
        }

        private void VisitName(int index, IdentifierNameSyntax name)
        {
            switch (model.GetSymbolInfo(name).Symbol)
            {
                case ITypeParameterSymbol parameter when map.IsMapped(parameter):
                    Rewrites[index] = new CopyRewrite(index, CopyRewriteKind.TypeArgument, Type: map.Substitute(parameter));
                    break;
                case IMethodSymbol { IsGenericMethod: true, ReducedFrom: null } method
                    when method.MethodKind is MethodKind.Ordinary or MethodKind.LocalFunction && !IsInNameOf(name):
                    var arguments = SubstituteAll(method.TypeArguments);
                    if (CheckWritable(index, arguments))
                    {
                        Rewrites[index] = new CopyRewrite(index, CopyRewriteKind.ExplicitTypeArguments,
                            TypeArguments: arguments);
                    }

                    break;
            }
        }

        private void VisitInvocation(int index, InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } &&
                model.GetSymbolInfo(invocation).Symbol is null)
            {
                if (model.GetConstantValue(invocation) is { HasValue: true, Value: string text } && NamesTypeParameter(invocation))
                {
                    Rewrites[index] = new CopyRewrite(index, CopyRewriteKind.NameOfConstant, Constant: text);
                }

                return;
            }

            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { ReducedFrom: not null } reduced)
            {
                return;
            }

            switch (invocation.Expression)
            {
                case MemberAccessExpressionSyntax access when map.DependsOnTypeParameter(model.GetTypeInfo(access.Expression).Type):
                    var method = reduced.GetConstructedReducedFrom() ?? reduced;
                    var arguments = SubstituteAll(method.TypeArguments);
                    if (CheckWritable(index, arguments))
                    {
                        Rewrites[index] = new CopyRewrite(index, CopyRewriteKind.StaticExtensionCall, Method: method,
                            TypeArguments: arguments);
                    }

                    break;
                case MemberBindingExpressionSyntax:
                    Problems.Add(new CopyProblem(index,
                        $"the conditional call of the extension method '{reduced.Name}' cannot be pinned"));
                    break;
            }
        }

        private void VisitConstrainedMember(int index, MemberAccessExpressionSyntax access)
        {
            var member = model.GetSymbolInfo(access).Symbol;
            if (Resolve(access, member) is not { } resolved)
            {
                return;
            }

            switch (resolved.Kind)
            {
                case ConstrainedMemberKind.IdentityConstant:
                    Rewrites[index] = new CopyRewrite(index, CopyRewriteKind.IdentityConstant, Type: resolved.ClosedType,
                        Constant: resolved.Constant);
                    break;
                case ConstrainedMemberKind.Unsupported:
                    Problems.Add(new CopyProblem(index, resolved.Reason ?? string.Empty));
                    break;
            }
        }

        private void VisitOperator(int index, ExpressionSyntax node)
        {
            if (Resolve(node, model.GetSymbolInfo(node).Symbol) is not { } resolved)
            {
                return;
            }

            if (resolved.Kind == ConstrainedMemberKind.Unsupported)
            {
                Problems.Add(new CopyProblem(index, resolved.Reason ?? string.Empty));
                return;
            }

            if (resolved.Kind == ConstrainedMemberKind.BuiltinOperator)
            {
                // Of the narrowed operators only +, -, * and negation throw in their checked form: checked / of
                // sbyte.MinValue by -1 gives sbyte.MinValue, as the unchecked one does
                var isChecked = (resolved.OperatorName?.StartsWith("op_Checked", StringComparison.Ordinal) ?? false) &&
                                node.Kind() is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or
                                    SyntaxKind.MultiplyExpression or SyntaxKind.UnaryMinusExpression;
                PlanBuiltinOperator(index, node, resolved.ClosedType, isChecked);
            }
        }

        // The C# operator of a built-in type computes in int for the small types: convert back where the generic math
        // operator returns the type
        private void PlanBuiltinOperator(int index, ExpressionSyntax node, ITypeSymbol closed, bool isChecked)
        {
            var kind = node.Kind();
            if (node is AssignmentExpressionSyntax)
            {
                // a op= b converts back to the type itself; only the shift count differs
                if (BuiltinNumerics.IsPromoted(closed) && kind is SyntaxKind.LeftShiftAssignmentExpression or
                        SyntaxKind.RightShiftAssignmentExpression or SyntaxKind.UnsignedRightShiftAssignmentExpression)
                {
                    Problems.Add(new CopyProblem(index,
                        $"a compound shift of '{closed.Name}' masks the count differently in C#; write the shift out"));
                }

                return;
            }

            if (kind == SyntaxKind.UnaryMinusExpression)
            {
                if (BuiltinNumerics.NegationLeavesType(closed))
                {
                    Rewrites[index] = new CopyRewrite(index, CopyRewriteKind.NarrowedNegation, Type: closed,
                        Checked: isChecked);
                }

                return;
            }

            if (!BuiltinNumerics.IsPromoted(closed))
            {
                return;
            }

            switch (kind)
            {
                case SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or SyntaxKind.MultiplyExpression or
                    SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression or SyntaxKind.BitwiseAndExpression or
                    SyntaxKind.BitwiseOrExpression or SyntaxKind.ExclusiveOrExpression or
                    SyntaxKind.BitwiseNotExpression or SyntaxKind.UnaryPlusExpression:
                    Rewrites[index] = new CopyRewrite(index, CopyRewriteKind.NarrowedOperator, Type: closed,
                        Checked: isChecked);
                    break;
                case SyntaxKind.LeftShiftExpression or SyntaxKind.RightShiftExpression or
                    SyntaxKind.UnsignedRightShiftExpression:
                    Rewrites[index] = new CopyRewrite(index, CopyRewriteKind.NarrowedShift, Type: closed,
                        Constant: BuiltinNumerics.GetShiftMask(closed));
                    break;
            }
        }

        private ConstrainedMember? Resolve(SyntaxNode node, ISymbol? member) =>
            ConstrainedAccess.IsConstrainable(member) && member is not null &&
            ConstrainedAccess.Find(node, model, member) is ITypeParameterSymbol parameter && map.IsMapped(parameter)
                ? map.ResolveConstrained(member, parameter)
                : null;

        private ImmutableArray<ITypeSymbol> SubstituteAll(ImmutableArray<ITypeSymbol> types)
        {
            var builder = ImmutableArray.CreateBuilder<ITypeSymbol>(types.Length);
            foreach (var type in types)
            {
                builder.Add(map.Substitute(type));
            }

            return builder.MoveToImmutable();
        }

        private bool CheckWritable(int index, ImmutableArray<ITypeSymbol> types)
        {
            foreach (var type in types)
            {
                if (!IsWritable(type))
                {
                    Problems.Add(new CopyProblem(index, $"the type argument '{type.Name}' cannot be written in C#"));
                    return false;
                }
            }

            return true;
        }

        private static bool IsWritable(ITypeSymbol type) =>
            type switch
            {
                { IsAnonymousType: true } or { TypeKind: TypeKind.Error } => false,
                IArrayTypeSymbol array => IsWritable(array.ElementType),
                IPointerTypeSymbol pointer => IsWritable(pointer.PointedAtType),
                INamedTypeSymbol named => named.TypeArguments
                    .AsStackEnumerable()
                    .All(IsWritable),
                _ => true,
            };

        private bool NamesTypeParameter(InvocationExpressionSyntax invocation) =>
            invocation.ArgumentList.DescendantNodes()
                .AsStackEnumerable()
                .OfType<IdentifierNameSyntax>()
                .Any((model, map), static (name, state) =>
                    state.model.GetSymbolInfo(name).Symbol is ITypeParameterSymbol parameter &&
                    state.map.IsMapped(parameter));

        private static bool IsInNameOf(SyntaxNode node) =>
            node.Ancestors()
                .AsStackEnumerable()
                .OfType<InvocationExpressionSyntax>()
                .Any(static invocation => invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });
    }
}
