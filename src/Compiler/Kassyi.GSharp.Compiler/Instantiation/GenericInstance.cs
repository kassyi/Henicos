namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// One closed instantiation of a generic kernel or device function: the method constructed with concrete type
/// arguments for its own type parameters and for those of every type that contains it.
/// </summary>
/// <remarks>
/// The constructed symbol is the identity: the C++ name of the instance derives from it
/// (<see cref="GenericInstanceNames.GetInstancePath"/>), so a call to <c>F&lt;int&gt;</c> and the closed copy of
/// <c>F</c> made for <c>int</c> (<see cref="GenericInstanceTable"/>) name the same function.
/// </remarks>
internal sealed class GenericInstance
{
    private GenericInstance(IMethodSymbol method, ImmutableArray<ITypeSymbol> typeArguments)
    {
        Method = method;
        TypeArguments = typeArguments;
    }

    /// <summary>The constructed method.</summary>
    public IMethodSymbol Method { get; }

    /// <summary>The generic declaration that <see cref="Method"/> was constructed from.</summary>
    public IMethodSymbol Definition => Method.OriginalDefinition;

    /// <summary>
    /// Every type argument, those of the outermost containing type first and the method's own last (the order of
    /// <see cref="TypeArgumentMap.Of"/>).
    /// </summary>
    public ImmutableArray<ITypeSymbol> TypeArguments { get; }

    /// <summary>
    /// The instance that <paramref name="method"/> stands for: a method constructed with closed type arguments, of a
    /// generic method or a member of a constructed generic type.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="method"/> is not generic, is the generic declaration itself, or has a type argument that is or
    /// contains a type parameter.
    /// </exception>
    public static GenericInstance Of(IMethodSymbol method)
    {
        var arguments = CollectTypeArguments(method);
        if (arguments.IsEmpty)
        {
            throw new ArgumentException(
                $"'{method.Name}' is not a generic method or a member of a generic type.", nameof(method));
        }

        foreach (var argument in arguments)
        {
            if (ContainsTypeParameter(argument))
            {
                throw new ArgumentException(
                    $"'{method.Name}' has the open type argument '{argument.GetFullyQualifiedCsharpName()}'.",
                    nameof(method));
            }
        }

        return new GenericInstance(method, arguments);
    }

    // Outermost containing type first, as TypeArgumentMap.Of walks them
    private static ImmutableArray<ITypeSymbol> CollectTypeArguments(IMethodSymbol method)
    {
        var levels = new Stack<ImmutableArray<ITypeSymbol>>();
        levels.Push(method.TypeArguments);
        for (var type = method.ContainingType; type != null; type = type.ContainingType)
        {
            levels.Push(type.TypeArguments);
        }

        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
        while (levels.Count > 0)
        {
            builder.AddRange(levels.Pop());
        }

        return builder.ToImmutable();
    }

    private static bool ContainsTypeParameter(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
            IPointerTypeSymbol pointer => ContainsTypeParameter(pointer.PointedAtType),
            INamedTypeSymbol named => named.TypeArguments
                    .AsStackEnumerable()
                    .Any(ContainsTypeParameter) ||
                (named.ContainingType is { } containing && ContainsTypeParameter(containing)),
            _ => false,
        };
}
