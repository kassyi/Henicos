namespace Kassyi.GSharp.Compiler.Transpiler;

// Routing (#711): a managed body that G# emits calls the managed version of a device function that has one
// (ManagedVersionAnalysis.HasManagedVersion) instead of the user's method, in every form that names it: a call F(a),
// T.F(a), F<int>(a), a reduced extension call x.F(a), and a method group passed on (Gpu.ForUnroll(0, n, F)). The other
// device functions stay the user's methods.
internal sealed partial class ManagedExecutionRewriter
{
    // The callee of a call that the rewriter writes itself (the target of an operator macro). An operator macro names
    // its target by attribute, so the target need not be accessible here: such a target is called through an
    // [UnsafeAccessor] stub (ManagedAccessorSet), which reaches its managed version when it has one
    private ExpressionSyntax SpellDeviceFunction(IMethodSymbol callee)
    {
        // [WHY] The stack form of a target that reaches a warp collective takes the offset of its frame first, and the
        // layout of the caller counts only the calls written in the source (WarpFrameLayout), not the operator the
        // macro replaces. The form of the caller cannot be made; the collector states why and the caller keeps its
        // state machine
        if (bodyKind == ManagedBodyKind.StackBody &&
            SuspensionAnalysis.ReachesWarpShuffle(callee, semanticModel.Compilation))
        {
            throw new InvalidOperationException(
                $"A stack form cannot call '{callee.Name}', which reaches a warp collective, " +
                "through an operator macro.");
        }

        var prefix = GetTargetPrefix(callee);
        if (accessors.IsAccessible(callee))
        {
            return SpellCallee(callee, typeArguments: null, prefix);
        }

        return accessors.Spell(callee, ManagedRouting.GetAccessorName(callee, prefix));
    }

    private string? GetTargetPrefix(IMethodSymbol method) =>
        ManagedRouting.GetTargetPrefix(method, semanticModel.Compilation, bodyKind);

    // global::<containing type>.<prefix><function path>[<type arguments>] when prefixed, otherwise
    // global::<containing type>.<name>[<type arguments>]
    private static ExpressionSyntax SpellCallee(IMethodSymbol callee, TypeArgumentListSyntax? typeArguments,
        string? prefix)
    {
        var identifier = SyntaxFactory.Identifier(ManagedRouting.GetMemberName(callee, prefix));
        SimpleNameSyntax name = typeArguments is null
            ? SyntaxFactory.IdentifierName(identifier)
            : SyntaxFactory.GenericName(identifier, typeArguments);
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.ParseExpression(callee.ContainingType.GetFullyQualifiedCsharpName()),
            name
        );
    }

    // A simple name (F, F<int>) that calls or names a routed device function becomes its target form; any other name
    // is qualified (ManagedNameQualifier). The name of a member access is routed with the member access
    // (TryRouteMemberAccess)
    private SyntaxNode? RouteOrQualify(SimpleNameSyntax node, SyntaxNode? visited)
    {
        if (node.Parent is not MemberAccessExpressionSyntax { Name: var memberName } || memberName != node)
        {
            if (TryGetRoutedMethod(node, out var method, out var prefix))
            {
                return SpellCallee(method, (visited as GenericNameSyntax)?.TypeArgumentList, prefix)
                    .WithTriviaFrom(node);
            }
        }

        return ManagedNameQualifier.QualifyName(node, visited, semanticModel);
    }

    // T.F or T.F<int> that calls or names a routed device function; a reduced extension method group x.F or x.F<int>
    // that names one (__gsharp_bind(x, global::T.<prefix>F), #940); null otherwise. A reduced extension call x.F(a) is
    // routed with the static call it becomes (RouteExtensionCall)
    private ExpressionSyntax? TryRouteMemberAccess(MemberAccessExpressionSyntax node, SyntaxNode? visited)
    {
        if (!node.IsKind(SyntaxKind.SimpleMemberAccessExpression))
        {
            return null;
        }

        var name = (visited as MemberAccessExpressionSyntax)?.Name ?? node.Name;
        var typeArguments = (name as GenericNameSyntax)?.TypeArgumentList;
        if (TryGetRoutedMethod(node, out var method, out var prefix))
        {
            return SpellCallee(method, typeArguments, prefix).WithTriviaFrom(node);
        }

        if ((node.Parent is not InvocationExpressionSyntax parentInvocation || parentInvocation.Expression != node) &&
            semanticModel.GetSymbol(node) is IMethodSymbol symbol &&
            ManagedRouting.GetBoundExtension(symbol, semanticModel.Compilation, bodyKind) is var (extension, extPrefix))
        {
            ThrowIfStackWarpGroup(node.Name, symbol);
            var receiver = (visited as MemberAccessExpressionSyntax)?.Expression ?? node.Expression;
            return SpellBindCall(receiver, symbol, extension, extPrefix, typeArguments).WithTriviaFrom(node);
        }

        return null;
    }

    // global::Kassyi.GSharp.Core.ManagedGpuContext.__gsharp_bind<<receiver type>, <parameter types>[, <return type>]>(<receiver>, <callee>)
    private static ExpressionSyntax SpellBindCall(
        ExpressionSyntax receiver,
        IMethodSymbol reduced,
        IMethodSymbol extension,
        string prefix,
        TypeArgumentListSyntax? typeArguments)
    {
        var typeArgumentsList = ManagedRouting.GetBindTypeArguments(reduced, extension);
        using var writer = new SourceWriter();
        writer.Append(ManagedNames.Context)
            .Append('.')
            .Append(GSharpInternalNames.ManagedVersion.Bind)
            .Append('<');
        writer.AppendCsharpTypeName(typeArgumentsList[0]);
        for (var i = 1; i < typeArgumentsList.Length; i++)
        {
            writer.Append(", ");
            writer.AppendCsharpTypeName(typeArgumentsList[i]);
        }

        writer.Append('>');
        var callee = SpellCallee(extension, typeArguments, prefix);
        var arguments = SyntaxFactory.SeparatedList<ArgumentSyntax>()
            .Add(SyntaxFactory.Argument(receiver.WithoutTrivia()))
            .Add(SyntaxFactory.Argument(callee));
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.ParseExpression(writer.ToString()),
            SyntaxFactory.ArgumentList(arguments));
    }

    // The static call global::N.C.F(x, a) that ManagedNameQualifier made of the reduced extension call x.F(a), with the
    // target form as its callee when F is routed
    private ExpressionSyntax RouteExtensionCall(InvocationExpressionSyntax node, ExpressionSyntax rewritten)
    {
        if (rewritten is not InvocationExpressionSyntax call ||
            semanticModel.GetSymbol(node) is not IMethodSymbol symbol ||
            ManagedRouting.GetRoutedExtensionCall(symbol, semanticModel.Compilation, bodyKind) is not
                var (extension, prefix))
        {
            return rewritten;
        }

        var typeArguments = (call.Expression as QualifiedNameSyntax)?.Right is GenericNameSyntax generic
            ? generic.TypeArgumentList
            : null;
        return call.WithExpression(SpellCallee(extension, typeArguments, prefix));
    }

    // A reduced extension method is routed with its call only (RouteExtensionCall)
    private bool TryGetRoutedMethod(ExpressionSyntax node, [NotNullWhen(true)] out IMethodSymbol? method,
        [NotNullWhen(true)] out string? prefix)
    {
        method = null;
        prefix = null;
        if (semanticModel.GetSymbol(node) is not IMethodSymbol symbol ||
            ManagedRouting.GetRoutedReferencePrefix(symbol, semanticModel.Compilation, bodyKind) is not
                { } targetPrefix)
        {
            return false;
        }

        ThrowIfStackWarpGroup(node, symbol);
        method = symbol;
        prefix = targetPrefix;
        return true;
    }

    // [WHY] The stack form of a device function that reaches a warp collective takes the offset of its frame first,
    // which a delegate cannot pass, and the layout of the caller counts only calls (WarpFrameLayout). The form of the
    // caller cannot be made; the collector states why and the caller keeps its state machine
    private void ThrowIfStackWarpGroup(ExpressionSyntax node, IMethodSymbol method)
    {
        if (bodyKind == ManagedBodyKind.StackBody &&
            CallTargetClassifier.IsMethodGroup(node) &&
            SuspensionAnalysis.ReachesWarpShuffle(method.ReducedFrom ?? method, semanticModel.Compilation))
        {
            throw new InvalidOperationException(
                $"A stack form cannot pass '{method.Name}', which reaches a warp collective, as a method group.");
        }
    }
}
