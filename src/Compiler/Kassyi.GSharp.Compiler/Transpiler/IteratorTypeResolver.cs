namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Resolves <c>[GpuDeviceFunction]</c> iterator methods (<c>IEnumerable&lt;T&gt;</c> with <c>yield</c>) and their
/// consumption expressions (<c>foreach</c> over a direct call or a single-assigned local variable).
/// </summary>
internal static class IteratorTypeResolver
{
    /// <summary>
    /// Determines whether <paramref name="type"/> is <see cref="System.Collections.Generic.IEnumerable{T}"/> and
    /// returns its element type <c>T</c>.
    /// </summary>
    public static bool IsGenericIEnumerable(
        ITypeSymbol? type,
        [NotNullWhen(true)] out ITypeSymbol? elementType)
    {
        if (type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T,
                TypeArguments.Length: 1
            } named)
        {
            elementType = named.TypeArguments[0];
            return true;
        }

        elementType = null;
        return false;
    }

    /// <summary>Formats a type symbol for diagnostic message text.</summary>
    public static string FormatType(ITypeSymbol type) =>
#pragma warning disable RS0030 // Diagnostic message text only (not a hot path)
        type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
#pragma warning restore RS0030

    /// <summary>Formats a syntax node for diagnostic message text.</summary>
    public static string FormatSyntax(SyntaxNode node) =>
#pragma warning disable RS0030 // Diagnostic message text only (not a hot path)
        node.ToString();
#pragma warning restore RS0030

    /// <summary>
    /// Checks whether <paramref name="elementType"/> is unsupported as the element type <c>T</c> of a GPU iterator and
    /// returns the diagnostic reason for GSH0070 when unsupported.
    /// </summary>
    public static bool TryGetUnsupportedElementTypeReason(
        ITypeSymbol elementType,
        [NotNullWhen(true)] out string? reason)
    {
        if (elementType is ITypeParameterSymbol)
        {
            reason = $"element type '{FormatType(elementType)}' cannot be a type parameter";
            return true;
        }

        if (elementType.IsSharedMemoryType())
        {
            reason = $"element type '{FormatType(elementType)}' cannot be SharedMemory<T>";
            return true;
        }

        if (elementType.IsRefLikeType)
        {
            reason = $"element type '{FormatType(elementType)}' cannot be a ref struct";
            return true;
        }

        if (elementType.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
        {
            reason = $"element type '{FormatType(elementType)}' cannot be a pointer";
            return true;
        }

        if (elementType.TypeKind == TypeKind.Array)
        {
            reason = $"element type '{FormatType(elementType)}' cannot be an array";
            return true;
        }

        if (!elementType.IsUnmanagedType)
        {
            reason = $"element type '{FormatType(elementType)}' must be an unmanaged value type";
            return true;
        }

        reason = null;
        return false;
    }

    /// <summary>
    /// Determines whether <paramref name="node"/> contains a <c>yield return</c> or <c>yield break</c> statement that
    /// is not nested inside a local function or anonymous function.
    /// </summary>
    public static bool ContainsDirectYield(SyntaxNode? node)
    {
        if (node is null)
        {
            return false;
        }

        foreach (var child in node.DescendantNodes(static n =>
                     n is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)))
        {
            if (child is YieldStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether <paramref name="node"/> or any nested local function / anonymous function contains a
    /// <c>yield return</c> or <c>yield break</c> statement.
    /// </summary>
    public static bool ContainsAnyYield(SyntaxNode? node)
    {
        if (node is null)
        {
            return false;
        }

        foreach (var child in node.DescendantNodes())
        {
            if (child is YieldStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether <paramref name="methodSymbol"/> is a valid static <c>[GpuDeviceFunction]</c> iterator
    /// returning <c>IEnumerable&lt;T&gt;</c> with a supported element type and a body containing <c>yield</c>.
    /// </summary>
    public static bool IsDeviceIteratorMethod([NotNullWhen(true)] IMethodSymbol? methodSymbol) =>
        methodSymbol is { IsStatic: true, IsGenericMethod: false } &&
        methodSymbol.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
        !methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
        IsGenericIEnumerable(methodSymbol.ReturnType, out var elementType) &&
        !TryGetUnsupportedElementTypeReason(elementType, out _) &&
        methodSymbol.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var decl) &&
        ContainsDirectYield(decl.Body);

    /// <summary>
    /// Determines whether <paramref name="expression"/> is an invocation of a <c>[GpuDeviceFunction]</c> whose body
    /// contains <c>yield</c> (whether valid or rejected by GSH0070).
    /// </summary>
    public static bool IsIteratorCandidateInvocation(ExpressionSyntax expression, SemanticModel semanticModel) =>
        UnwrapParentheses(expression) is InvocationExpressionSyntax inv &&
        semanticModel.GetSymbol(inv) is IMethodSymbol method &&
        method.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
        method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var decl) &&
        ContainsDirectYield(decl.Body);

    /// <summary>
    /// Determines whether <paramref name="methodSymbol"/> is a static <c>[GpuDeviceFunction]</c> returning
    /// <c>IEnumerable&lt;T&gt;</c> whose body is not available in the current compilation (for example, from a
    /// referenced assembly).
    /// </summary>
    public static bool IsExternalIteratorCandidate([NotNullWhen(true)] IMethodSymbol? methodSymbol) =>
        methodSymbol is
        {
            IsStatic: true,
            IsGenericMethod: false,
            DeclaringSyntaxReferences.IsEmpty: true
        } &&
        methodSymbol.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
        !methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
        IsGenericIEnumerable(methodSymbol.ReturnType, out _);

    /// <summary>
    /// Resolves <paramref name="expression"/> (either a direct invocation or a local variable initialized from an
    /// invocation) when it targets a <c>[GpuDeviceFunction]</c> returning <c>IEnumerable&lt;T&gt;</c> from an external
    /// assembly without source syntax in this compilation.
    /// </summary>
    public static bool TryResolveExternalIteratorSource(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        [NotNullWhen(true)] out IMethodSymbol? methodSymbol)
    {
        var unwrapped = UnwrapParentheses(expression);
        if (unwrapped is InvocationExpressionSyntax inv &&
            semanticModel.GetSymbol(inv) is IMethodSymbol directMethod &&
            IsExternalIteratorCandidate(directMethod))
        {
            methodSymbol = directMethod;
            return true;
        }

        if (unwrapped is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbol(identifier) is ILocalSymbol local &&
            local.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) &&
            declarator.Initializer?.Value is { } initializerValue &&
            UnwrapParentheses(initializerValue) is InvocationExpressionSyntax initInv &&
            semanticModel.GetSymbol(initInv) is IMethodSymbol initMethod &&
            IsExternalIteratorCandidate(initMethod))
        {
            methodSymbol = initMethod;
            return true;
        }

        methodSymbol = null;
        return false;
    }

    /// <summary>
    /// Determines whether an iterator <paramref name="methodSymbol"/> calls itself directly or mutually through other
    /// device functions.
    /// </summary>
    public static bool IsRecursiveIterator(IMethodSymbol methodSymbol, Compilation compilation)
    {
        var root = methodSymbol.OriginalDefinition;
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        return ReachesTarget(root, root, compilation, visited);
    }

    private static bool ReachesTarget(
        IMethodSymbol current,
        IMethodSymbol target,
        Compilation compilation,
        HashSet<IMethodSymbol> visited)
    {
        if (!current.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var decl) ||
            decl.Body is null ||
            !compilation.ContainsSyntaxTree(decl.SyntaxTree))
        {
            return false;
        }

        var model = compilation.GetSemanticModel(decl.SyntaxTree);
        foreach (var node in decl.Body.DescendantNodes())
        {
            if (node is not InvocationExpressionSyntax invocation ||
                model.GetSymbol(invocation) is not IMethodSymbol callee)
            {
                continue;
            }

            var definition = callee.OriginalDefinition;
            if (definition.IsEqualTo(target))
            {
                return true;
            }

            if (visited.Add(definition) && ReachesTarget(definition, target, compilation, visited))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Strips outer <see cref="ParenthesizedExpressionSyntax"/> wrappers from <paramref name="expression"/>.
    /// </summary>
    public static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        var current = expression;
        while (current is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized.Expression;
        }

        return current;
    }

    /// <summary>
    /// Resolves <paramref name="expression"/> as a direct invocation of a valid <c>[GpuDeviceFunction]</c> iterator.
    /// </summary>
    public static bool TryResolveIteratorInvocation(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        [NotNullWhen(true)] out InvocationExpressionSyntax? invocation,
        [NotNullWhen(true)] out IMethodSymbol? iteratorMethod)
    {
        if (UnwrapParentheses(expression) is InvocationExpressionSyntax inv &&
            semanticModel.GetSymbol(inv) is IMethodSymbol method &&
            IsDeviceIteratorMethod(method))
        {
            invocation = inv;
            iteratorMethod = method;
            return true;
        }

        invocation = null;
        iteratorMethod = null;
        return false;
    }

    /// <summary>
    /// Resolves <paramref name="expression"/> (either a direct iterator invocation or a local variable initialized from
    /// an iterator invocation) to the underlying invocation and iterator method symbol.
    /// </summary>
    public static bool TryResolveIteratorSource(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        [NotNullWhen(true)] out InvocationExpressionSyntax? invocation,
        [NotNullWhen(true)] out IMethodSymbol? iteratorMethod)
    {
        var unwrapped = UnwrapParentheses(expression);
        if (TryResolveIteratorInvocation(unwrapped, semanticModel, out invocation, out iteratorMethod))
        {
            return true;
        }

        if (unwrapped is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbol(identifier) is ILocalSymbol local &&
            local.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) &&
            declarator.Initializer?.Value is { } initializerValue &&
            TryResolveIteratorInvocation(initializerValue, semanticModel, out invocation, out iteratorMethod))
        {
            return true;
        }

        invocation = null;
        iteratorMethod = null;
        return false;
    }

    /// <summary>
    /// Determines whether <paramref name="expression"/> produces or references an iterator value (a direct call to a
    /// device iterator or a local variable of type <c>IEnumerable&lt;T&gt;</c>).
    /// </summary>
    public static bool IsIteratorValueExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var unwrapped = UnwrapParentheses(expression);
        if (TryResolveIteratorInvocation(unwrapped, semanticModel, out _, out _))
        {
            return true;
        }

        return unwrapped is IdentifierNameSyntax identifier &&
               semanticModel.GetSymbol(identifier) is ILocalSymbol local &&
               IsGenericIEnumerable(local.Type, out _);
    }

    /// <summary>
    /// Determines whether <paramref name="expression"/> is an iterator source in a <c>foreach</c> statement (so
    /// <c>ForEachNotSupportedRule</c> does not report GSH0007 on it).
    /// </summary>
    public static bool IsIteratorForEachSource(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var unwrapped = UnwrapParentheses(expression);
        if (TryResolveIteratorSource(unwrapped, semanticModel, out _, out _) ||
            TryResolveExternalIteratorSource(unwrapped, semanticModel, out _) ||
            IsIteratorCandidateInvocation(unwrapped, semanticModel))
        {
            return true;
        }

        if (unwrapped is not IdentifierNameSyntax identifier ||
            semanticModel.GetSymbol(identifier) is not ILocalSymbol local ||
            !local.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) ||
            declarator.Initializer?.Value is not { } initializerValue)
        {
            return false;
        }

        var unwrappedInit = UnwrapParentheses(initializerValue);
        return IsIteratorCandidateInvocation(unwrappedInit, semanticModel) ||
               unwrappedInit switch
               {
                   ConditionalExpressionSyntax conditional =>
                       IsIteratorValueExpression(conditional.WhenTrue, semanticModel) ||
                       IsIteratorValueExpression(conditional.WhenFalse, semanticModel),
                   BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } binary =>
                       IsIteratorValueExpression(binary.Left, semanticModel) ||
                       IsIteratorValueExpression(binary.Right, semanticModel),
                   AssignmentExpressionSyntax assignment =>
                       IsIteratorValueExpression(assignment.Left, semanticModel) ||
                       IsIteratorValueExpression(assignment.Right, semanticModel),
                   _ => false
               };
    }
}
