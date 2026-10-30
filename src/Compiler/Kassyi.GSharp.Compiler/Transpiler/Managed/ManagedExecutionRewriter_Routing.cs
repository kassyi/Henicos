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
        var routed = IsRouted(callee);
        if (accessors.IsAccessible(callee))
        {
            return SpellCallee(callee, typeArguments: null, routed);
        }

        return accessors.Spell(callee, routed
            ? GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.ManagedVersion.MethodPrefix, callee)
            : callee.Name);
    }

    private bool IsRouted(IMethodSymbol method) =>
        ManagedVersionAnalysis.HasManagedVersion(method, semanticModel.Compilation);

    // global::<containing type>.__gsharp_managed_<function path>[<type arguments>] when routed, otherwise
    // global::<containing type>.<name>[<type arguments>]
    private static ExpressionSyntax SpellCallee(IMethodSymbol callee, TypeArgumentListSyntax? typeArguments,
        bool routed)
    {
        var identifier = routed
            ? SyntaxFactory.Identifier(
                GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.ManagedVersion.MethodPrefix, callee))
            : SyntaxFactory.Identifier(callee.Name.ToCSharpIdentifier());
        SimpleNameSyntax name = typeArguments is null
            ? SyntaxFactory.IdentifierName(identifier)
            : SyntaxFactory.GenericName(identifier, typeArguments);
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.ParseExpression(callee.ContainingType.GetFullyQualifiedCsharpName()),
            name
        );
    }

    // A simple name (F, F<int>) that calls or names a device function with a managed version becomes the managed
    // version; any other name is qualified (ManagedNameQualifier). The name of a member access is routed with the
    // member access (TryRouteMemberAccess)
    private SyntaxNode? RouteOrQualify(SimpleNameSyntax node, SyntaxNode? visited)
    {
        if (node.Parent is not MemberAccessExpressionSyntax { Name: var memberName } || memberName != node)
        {
            if (TryGetRoutedMethod(node, out var method))
            {
                return SpellCallee(method, (visited as GenericNameSyntax)?.TypeArgumentList, routed: true)
                    .WithTriviaFrom(node);
            }
        }

        return ManagedNameQualifier.QualifyName(node, visited, semanticModel);
    }

    // T.F or T.F<int> that calls or names a device function with a managed version; null otherwise. A reduced extension
    // call x.F(a) is routed with the static call it becomes (RouteExtensionCall)
    private ExpressionSyntax? TryRouteMemberAccess(MemberAccessExpressionSyntax node, SyntaxNode? visited)
    {
        if (!node.IsKind(SyntaxKind.SimpleMemberAccessExpression) || !TryGetRoutedMethod(node, out var method))
        {
            return null;
        }

        var name = (visited as MemberAccessExpressionSyntax)?.Name ?? node.Name;
        return SpellCallee(method, (name as GenericNameSyntax)?.TypeArgumentList, routed: true).WithTriviaFrom(node);
    }

    // The static call global::N.C.F(x, a) that ManagedNameQualifier made of the reduced extension call x.F(a), with the
    // managed version as its callee when F has one
    private ExpressionSyntax RouteExtensionCall(InvocationExpressionSyntax node, ExpressionSyntax rewritten)
    {
        if (rewritten is not InvocationExpressionSyntax call ||
            semanticModel.GetSymbol(node) is not IMethodSymbol { ReducedFrom: { } extension } ||
            !IsRouted(extension))
        {
            return rewritten;
        }

        var typeArguments = (call.Expression as QualifiedNameSyntax)?.Right is GenericNameSyntax generic
            ? generic.TypeArgumentList
            : null;
        return call.WithExpression(SpellCallee(extension, typeArguments, routed: true));
    }

    // A reduced extension method is routed with its call only (RouteExtensionCall)
    private bool TryGetRoutedMethod(ExpressionSyntax node, [NotNullWhen(true)] out IMethodSymbol? method)
    {
        method = null;
        if (semanticModel.GetSymbol(node) is not IMethodSymbol { ReducedFrom: null } symbol ||
            !symbol.HasAttribute(KnownAttributes.GpuDeviceFunction) ||
            !IsRouted(symbol))
        {
            return false;
        }

        method = symbol;
        return true;
    }
}
