namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Decides how the closed copy of a generic declaration is written: which names become the type arguments, and which
/// calls and operators are pinned to what the declaration binds, so that the copy does not bind to something else once
/// the type parameters are replaced (<see cref="CopyRewrite"/>). Nothing is written here
/// (<see cref="GenericCopyEmitter"/> writes the copy from the plan).
/// </summary>
internal static class GenericCopyPlanner
{
    /// <summary>
    /// Plans the copy of <paramref name="declaration"/> for the instance <paramref name="map"/> reads.
    /// </summary>
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

        return new GenericCopyPlan(instance, plan.Rewrites.ToImmutable(), plan.Problems.ToImmutable(),
            plan.Helpers.ToImmutable());
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

        public ImmutableArray<ConstrainedHelper>.Builder Helpers { get; } =
            ImmutableArray.CreateBuilder<ConstrainedHelper>();

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
                    VisitMemberAccess(index, access);
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
                    Rewrites[index] = new CopyRewrite.TypeArgument(index, map.Substitute(parameter));
                    break;
                case IMethodSymbol { IsGenericMethod: true, ReducedFrom: null } method
                    when method.MethodKind is MethodKind.Ordinary or MethodKind.LocalFunction && !IsInNameOf(name):
                    var arguments = SubstituteAll(method.TypeArguments);
                    if (CheckWritable(index, arguments))
                    {
                        Rewrites[index] = new CopyRewrite.ExplicitTypeArguments(index, arguments);
                    }

                    break;
            }
        }

        private void VisitInvocation(int index, InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } &&
                model.GetSymbolInfo(invocation).Symbol is null)
            {
                if (model.GetConstantValue(invocation) is { HasValue: true, Value: string text } &&
                    NamesTypeParameter(invocation))
                {
                    Rewrites[index] = new CopyRewrite.NameOf(index, text);
                }

                return;
            }

            if (invocation.Expression is MemberAccessExpressionSyntax callee &&
                (PlanConstrainedCall(index, callee) || PlanClosedCall(index, invocation, callee)))
            {
                return;
            }

            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { ReducedFrom: not null } reduced)
            {
                return;
            }

            switch (invocation.Expression)
            {
                case MemberAccessExpressionSyntax access
                    when map.DependsOnTypeParameter(model.GetTypeInfo(access.Expression).Type):
                    var method = reduced.GetConstructedReducedFrom() ?? reduced;
                    var arguments = SubstituteAll(method.TypeArguments);
                    if (CheckWritable(index, arguments))
                    {
                        Rewrites[index] = new CopyRewrite.StaticExtensionCall(index, method, arguments);
                    }

                    break;
                case MemberBindingExpressionSyntax:
                    Problems.Add(new CopyProblem(index,
                        $"the conditional call of the extension method '{reduced.Name}' cannot be pinned"));
                    break;
            }
        }

        // x.M(a) through a type parameter whose implementation is explicit or a default one: a helper makes the
        // constrained call, as C# does, and the copy calls the helper with the receiver by reference
        private bool PlanConstrainedCall(int index, MemberAccessExpressionSyntax callee)
        {
            if (ResolveInstance(callee) is not ConstrainedMember.ConstrainedCall call)
            {
                return false;
            }

            if (!IsVariable(callee.Expression))
            {
                Problems.Add(new CopyProblem(index,
                    $"the receiver '{callee.Expression}' of '{callee.Name}' is not a variable that a helper can take " +
                    "by reference; call it on a local"));
                return true;
            }

            var helper = new ConstrainedHelper(call.ClosedType, call.Member);
            var position = IndexOf(helper);
            if (position < 0)
            {
                Helpers.Add(helper);
                position = Helpers.Count - 1;
            }

            Rewrites[index] = new CopyRewrite.ConstrainedCall(index, position);
            return true;
        }

        private int IndexOf(ConstrainedHelper helper)
        {
            for (var i = 0; i < Helpers.Count; i++)
            {
                if (Helpers[i].ClosedType.IsEqualTo(helper.ClosedType) && Helpers[i].Member.IsEqualTo(helper.Member))
                {
                    return i;
                }
            }

            return -1;
        }

        // A local, a parameter that is not an in parameter, a field that is not readonly, or an element of an array:
        // what a helper can take by reference
        private bool IsVariable(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    return IsVariable(parenthesized.Expression);
                case ElementAccessExpressionSyntax element:
                    return model.GetTypeInfo(element.Expression).Type is IArrayTypeSymbol;
                case IdentifierNameSyntax or MemberAccessExpressionSyntax:
                    return model.GetSymbolInfo(expression).Symbol switch
                    {
                        ILocalSymbol local => !local.IsConst,
                        IParameterSymbol parameter => parameter.RefKind != RefKind.In,
                        IFieldSymbol field => !field.IsReadOnly && !field.IsConst,
                        _ => false,
                    };
                default:
                    return false;
            }
        }

        // T.Abs(x) of an unsigned type and the other generic math calls of the BCL that have a closed form
        private bool PlanClosedCall(
            int index,
            InvocationExpressionSyntax invocation,
            MemberAccessExpressionSyntax callee)
        {
            if (ResolveStatic(callee) is not { } resolved)
            {
                return false;
            }

            switch (resolved)
            {
                case ConstrainedMember.ArgumentCall:
                    Rewrites[index] = new CopyRewrite.ArgumentCall(index);
                    return true;
                case ConstrainedMember.FalseCall when IsSideEffectFree(invocation.ArgumentList):
                    Rewrites[index] = new CopyRewrite.FalseCall(index);
                    return true;
                case ConstrainedMember.FalseCall:
                    Problems.Add(new CopyProblem(index,
                        "the argument has side effects that a constant result would drop"));
                    return true;
                case ConstrainedMember.MathCall math:
                    Rewrites[index] = new CopyRewrite.MathCall(index, math.Name, math.ResultType);
                    return true;
                default:
                    return false;
            }
        }

        private void VisitMemberAccess(int index, MemberAccessExpressionSyntax access)
        {
            if (ResolveInstance(access) is { } instance)
            {
                // A call through a helper is planned at the call (PlanConstrainedCall)
                if (instance is ConstrainedMember.ConstrainedCall && IsCalled(access))
                {
                    return;
                }

                if (instance is not ConstrainedMember.Member)
                {
                    Problems.Add(new CopyProblem(index, (instance as ConstrainedMember.Unsupported)?.Reason ??
                                                       $"'{access.Name}' has no closed spelling in the copy"));
                }

                return;
            }

            switch (ResolveStatic(access))
            {
                case ConstrainedMember.IdentityConstant constant:
                    Rewrites[index] = new CopyRewrite.IdentityConstant(index, constant.ClosedType, constant.Value,
                        IsOperandOfConstantOperation(access));
                    break;
                case ConstrainedMember.StaticMember member:
                    Rewrites[index] = new CopyRewrite.StaticMember(index, member.ClosedType, member.Name,
                        IsOperandOfConstantOperation(access));
                    break;
                case ConstrainedMember.Unsupported unsupported:
                    Problems.Add(new CopyProblem(index, unsupported.Reason));
                    break;
                case ConstrainedMember.ArgumentCall or ConstrainedMember.FalseCall or ConstrainedMember.MathCall
                    when !IsCalled(access):
                    Problems.Add(new CopyProblem(index,
                        $"'{access.Name}' of '{access.Expression}' has a closed form for a call only"));
                    break;
            }
        }

        private void VisitOperator(int index, ExpressionSyntax node)
        {
            switch (ResolveStatic(node, model.GetSymbolInfo(node).Symbol))
            {
                case ConstrainedMember.Unsupported unsupported:
                    Problems.Add(new CopyProblem(index, unsupported.Reason));
                    break;
                case ConstrainedMember.BuiltinOperator builtin:
                    // Of the narrowed operators only +, -, * and negation throw in their checked form: checked / of
                    // sbyte.MinValue by -1 gives sbyte.MinValue, as the unchecked one does
                    var isChecked = builtin.OperatorName.StartsWith("op_Checked", StringComparison.Ordinal) &&
                                    node.Kind() is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or
                                        SyntaxKind.MultiplyExpression or SyntaxKind.UnaryMinusExpression;
                    PlanBuiltinOperator(index, node, builtin.ClosedType, isChecked);
                    break;
            }
        }

        // The C# operator of a built-in type computes in int for the small types: convert back where the generic math
        // operator returns the type
        private void PlanBuiltinOperator(int index, ExpressionSyntax node, ITypeSymbol closed, bool isChecked)
        {
            if (node is AssignmentExpressionSyntax assignment)
            {
                PlanCompoundAssignment(index, assignment, closed);
                return;
            }

            var kind = node.Kind();
            if (kind == SyntaxKind.UnaryMinusExpression)
            {
                PlanNegation(index, closed, isChecked);
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
                    Rewrites[index] = new CopyRewrite.NarrowedOperator(index, closed,
                        BuiltinNumerics.GetNarrowing(closed, isChecked));
                    break;
                case SyntaxKind.LeftShiftExpression or SyntaxKind.RightShiftExpression or
                    SyntaxKind.UnsignedRightShiftExpression:
                    Rewrites[index] = new CopyRewrite.NarrowedShift(index, closed, BuiltinNumerics.GetShiftMask(closed),
                        kind == SyntaxKind.UnsignedRightShiftExpression
                            ? BuiltinNumerics.GetUnsignedShiftOperandMask(closed)
                            : null,
                        BuiltinNumerics.GetNarrowing(closed, isChecked: false));
                    break;
            }
        }

        private void PlanNegation(int index, ITypeSymbol closed, bool isChecked)
        {
            if (!BuiltinNumerics.NegationLeavesType(closed))
            {
                return;
            }

            Rewrites[index] = BuiltinNumerics.IsPromoted(closed)
                ? new CopyRewrite.NarrowedNegation(index, closed, BuiltinNumerics.GetNarrowing(closed, isChecked))
                : new CopyRewrite.TypedNegation(index, closed);
        }

        // a /= b of a promoted type throws in a checked context where the generic one gives the wrapped value; a shift
        // masks its count with 31 where the generic one masks it with the width of the type
        private void PlanCompoundAssignment(int index, AssignmentExpressionSyntax assignment, ITypeSymbol closed)
        {
            if (!BuiltinNumerics.IsPromoted(closed) ||
                assignment.Kind() is not (SyntaxKind.DivideAssignmentExpression or
                    SyntaxKind.LeftShiftAssignmentExpression or SyntaxKind.RightShiftAssignmentExpression or
                    SyntaxKind.UnsignedRightShiftAssignmentExpression))
            {
                return;
            }

            if (!IsSideEffectFree(assignment.Left))
            {
                Problems.Add(new CopyProblem(index,
                    $"the target of '{assignment.OperatorToken.Text}' has side effects and is read twice in the " +
                    $"copy for '{closed.Name}'; write the operation out"));
                return;
            }

            var narrowing = BuiltinNumerics.GetNarrowing(closed, isChecked: false);
            Rewrites[index] = assignment.Kind() == SyntaxKind.DivideAssignmentExpression
                ? new CopyRewrite.NarrowedCompoundDivision(index, closed, narrowing)
                : new CopyRewrite.NarrowedCompoundShift(
                    index,
                    closed,
                    GetBinaryKind(assignment.Kind()),
                    BuiltinNumerics.GetShiftMask(closed),
                    assignment.Kind() == SyntaxKind.UnsignedRightShiftAssignmentExpression
                        ? BuiltinNumerics.GetUnsignedShiftOperandMask(closed)
                        : null,
                    narrowing);
        }

        private static SyntaxKind GetBinaryKind(SyntaxKind assignment) =>
            assignment switch
            {
                SyntaxKind.LeftShiftAssignmentExpression => SyntaxKind.LeftShiftExpression,
                SyntaxKind.RightShiftAssignmentExpression => SyntaxKind.RightShiftExpression,
                _ => SyntaxKind.UnsignedRightShiftExpression,
            };

        // The static abstract or virtual member of an interface that a node reaches through a mapped type parameter
        private ConstrainedMember? ResolveStatic(MemberAccessExpressionSyntax access) =>
            ResolveStatic(access, model.GetSymbolInfo(access).Symbol);

        private ConstrainedMember? ResolveStatic(SyntaxNode node, ISymbol? member) =>
            ConstrainedAccess.IsConstrainable(member) && member is not null &&
            ConstrainedAccess.Find(node, model, member) is ITypeParameterSymbol parameter && map.IsMapped(parameter)
                ? map.ResolveConstrained(member, parameter)
                : null;

        // The instance member of an interface that a node reaches through a mapped type parameter (x.M() on T : IFoo)
        private ConstrainedMember? ResolveInstance(MemberAccessExpressionSyntax access) =>
            model.GetSymbolInfo(access).Symbol is { } member && ConstrainedAccess.IsInterfaceInstanceMember(member) &&
            model.GetTypeInfo(access.Expression).Type is ITypeParameterSymbol parameter && map.IsMapped(parameter)
                ? map.ResolveConstrained(member, parameter)
                : null;

        // Whether the access is the callee of a call (T.Abs(x), not the method group T.Abs)
        private static bool IsCalled(MemberAccessExpressionSyntax access) =>
            access.Parent is InvocationExpressionSyntax invocation && invocation.Expression == access;

        // An operator on two constants is folded at compile time, where the copy needs the run-time exception of the
        // declaration for a division by zero or an overflow: such an operand is written so that C# cannot fold it
        private bool IsOperandOfConstantOperation(MemberAccessExpressionSyntax access)
        {
            SyntaxNode operand = access;
            while (operand.Parent is ParenthesizedExpressionSyntax parenthesized)
            {
                operand = parenthesized;
            }

            if (operand.Parent is not BinaryExpressionSyntax binary ||
                binary.Kind() is not (SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or
                    SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression))
            {
                return false;
            }

            return IsConstantLike(binary.Left == operand ? binary.Right : binary.Left);
        }

        private bool IsConstantLike(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }

            return model.GetConstantValue(expression).HasValue ||
                   (expression is MemberAccessExpressionSyntax access &&
                    ResolveStatic(access) is ConstrainedMember.IdentityConstant or ConstrainedMember.StaticMember);
        }

        // A form that reads or evaluates it any number of times gives what it gives once
        private static bool IsSideEffectFree(SyntaxNode node) =>
            node switch
            {
                IdentifierNameSyntax or LiteralExpressionSyntax or ThisExpressionSyntax => true,
                ParenthesizedExpressionSyntax parenthesized => IsSideEffectFree(parenthesized.Expression),
                MemberAccessExpressionSyntax access => IsSideEffectFree(access.Expression),
                ElementAccessExpressionSyntax element => IsSideEffectFree(element.Expression) &&
                                                         IsSideEffectFree(element.ArgumentList),
                BracketedArgumentListSyntax or ArgumentListSyntax => ((BaseArgumentListSyntax)node).Arguments
                    .AsStackEnumerable()
                    .All(static argument => IsSideEffectFree(argument.Expression)),
                _ => false,
            };

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
                .Any(static invocation =>
                    invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });
    }
}
