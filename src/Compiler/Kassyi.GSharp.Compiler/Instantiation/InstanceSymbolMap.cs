namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Reads a symbol of a generic declaration as the instance reads it: each type parameter of the declaration and of the
/// types that contain it replaced by its type argument, inside arrays, pointers and constructed types too.
/// </summary>
/// <remarks>
/// The symbols stay in the compilation of the declaration. Unlike <see cref="TypeArgumentMap"/>, which reads the
/// generic structs of a kernel, every type is followed to the bottom, since the closed copy is compared with it.
/// </remarks>
internal sealed class InstanceSymbolMap
{
    private readonly Dictionary<ITypeParameterSymbol, ITypeSymbol> _arguments =
        new(SymbolEqualityComparer.Default);

    private readonly Compilation _compilation;

    public InstanceSymbolMap(GenericInstance instance, Compilation compilation)
    {
        _compilation = compilation;
        var index = 0;
        foreach (var parameter in CollectTypeParameters(instance.Definition))
        {
            _arguments[parameter] = instance.TypeArguments[index++];
        }
    }

    /// <summary>Whether <paramref name="parameter"/> is one the instance gives an argument for.</summary>
    public bool IsMapped(ITypeParameterSymbol parameter) => _arguments.ContainsKey(parameter.OriginalDefinition);

    /// <summary>Whether <paramref name="type"/> is or contains a type parameter the instance gives an argument for.</summary>
    public bool DependsOnTypeParameter(ITypeSymbol? type) =>
        type is not null && !ReferenceEquals(Substitute(type), type);

    /// <summary><paramref name="type"/> with the mapped type parameters replaced; itself when none occurs in it.</summary>
    public ITypeSymbol Substitute(ITypeSymbol type)
    {
        switch (type)
        {
            case ITypeParameterSymbol parameter:
                return _arguments.TryGetValue(parameter.OriginalDefinition, out var argument) ? argument : type;
            case IArrayTypeSymbol array:
                var element = Substitute(array.ElementType);
                return ReferenceEquals(element, array.ElementType)
                    ? type
                    : _compilation.CreateArrayTypeSymbol(element, array.Rank);
            case IPointerTypeSymbol pointer:
                var pointed = Substitute(pointer.PointedAtType);
                return ReferenceEquals(pointed, pointer.PointedAtType)
                    ? type
                    : _compilation.CreatePointerTypeSymbol(pointed);
            case INamedTypeSymbol named:
                return SubstituteNamed(named);
            default:
                return type;
        }
    }

    /// <summary>
    /// <paramref name="symbol"/>, read in the generic declaration, as the instance reads it: a type substituted, a
    /// member of a constructed type taken from the substituted type, a generic method constructed with the substituted
    /// type arguments, and the call of an extension method in its static form.
    /// </summary>
    /// <remarks>
    /// Locals, parameters, lambdas and local functions are left as they are: they are declared inside the declaration
    /// and are matched with the copy by their declaration (<see cref="CopySymbolComparer"/>).
    /// </remarks>
    public ISymbol? Substitute(ISymbol? symbol)
    {
        switch (symbol)
        {
            case null:
                return null;
            case ITypeSymbol type:
                return Substitute(type);
            case IMethodSymbol method:
                return SubstituteMethod(method.GetConstructedReducedFrom() ?? method);
            case IPropertySymbol or IFieldSymbol or IEventSymbol:
                return symbol.ContainingType is { } containing &&
                       Substitute(containing) is INamedTypeSymbol substituted &&
                       !ReferenceEquals(substituted, containing)
                    ? FindMember(substituted, symbol) ?? symbol
                    : symbol;
            default:
                return symbol;
        }
    }

    /// <summary>
    /// What a member of an interface that the declaration reaches through a type parameter (a static abstract or virtual
    /// member, <c>T.Zero</c>, <c>a + b</c> on <c>T : INumber&lt;T&gt;</c>) is for the type argument, and how the copy
    /// must bind it.
    /// </summary>
    /// <param name="member">The interface member, as the declaration binds it.</param>
    /// <param name="constrainedTo">The type parameter the member is reached through.</param>
    public ConstrainedMember ResolveConstrained(ISymbol member, ITypeSymbol constrainedTo)
    {
        var closed = Substitute(constrainedTo);
        return Substitute(member) is { } mapped
            ? Resolve(mapped, closed)
            : ConstrainedMember.Unsupported(closed, "the member does not exist in the instance");
    }

    private ConstrainedMember Resolve(ISymbol member, ITypeSymbol closed)
    {
        var implementation = closed.FindImplementationForInterfaceMember(member);
        if (implementation is null)
        {
            return ConstrainedMember.Unsupported(closed, $"'{closed.Name}' does not implement '{member.Name}'");
        }

        if (implementation.ContainingType is { TypeKind: TypeKind.Interface } interfaceType)
        {
            // A checked operator that the type does not declare runs the default implementation of the interface,
            // which calls the unchecked operator: the copy binds that one
            return member is IMethodSymbol { Name: var name } && TryGetUncheckedOperator(interfaceType, name) is { } plain
                ? Resolve(plain, closed)
                : ConstrainedMember.Unsupported(closed,
                    $"'{member.Name}' runs the default implementation of '{interfaceType.Name}'");
        }

        // A public member is bound by its name whether or not it also declares the interface member it implements
        // (the BCL's int.Max does: it is public and lists INumber<int>.Max); only a private explicit implementation
        // has no spelling of its own
        var target = GetExplicitTarget(implementation);
        if (implementation.DeclaredAccessibility == Accessibility.Public || target is null)
        {
            return ConstrainedMember.Member(closed, implementation);
        }

        if (BuiltinNumerics.IsBuiltinNumeric(closed))
        {
            // The interface declares the operator as a static abstract (or virtual) member named op_*
            if (target is IMethodSymbol op && op.Name.StartsWith("op_", StringComparison.Ordinal))
            {
                return ConstrainedMember.BuiltinOperator(closed, op.Name);
            }

            if (target is IPropertySymbol property && BuiltinNumerics.TryGetIdentity(property.Name, out var identity))
            {
                return ConstrainedMember.IdentityConstant(closed, identity);
            }
        }

        return ConstrainedMember.Unsupported(closed,
            $"'{closed.Name}' implements '{target.Name}' explicitly; implement it as a public member");
    }

    // op_CheckedAddition -> op_Addition of the same interface
    private static IMethodSymbol? TryGetUncheckedOperator(INamedTypeSymbol interfaceType, string checkedName)
    {
        const string CheckedPrefix = "op_Checked";
        if (!checkedName.StartsWith(CheckedPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var plainName = "op_" + checkedName[CheckedPrefix.Length..];
        return interfaceType.GetMembers(plainName)
            .AsStackEnumerable()
            .OfType<IMethodSymbol>()
            .FirstOrDefault();
    }

    private static ISymbol? GetExplicitTarget(ISymbol implementation) =>
        implementation switch
        {
            IMethodSymbol { ExplicitInterfaceImplementations: [var target, ..] } => target,
            IPropertySymbol { ExplicitInterfaceImplementations: [var target, ..] } => target,
            IEventSymbol { ExplicitInterfaceImplementations: [var target, ..] } => target,
            _ => null,
        };

    private ISymbol SubstituteMethod(IMethodSymbol method)
    {
        if (method.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction)
        {
            return method;
        }

        var result = method;
        if (method.ContainingType is { } containing &&
            Substitute(containing) is INamedTypeSymbol substituted && !ReferenceEquals(substituted, containing))
        {
            if (FindMember(substituted, method) is not IMethodSymbol member)
            {
                return method;
            }

            result = member;
        }

        if (!method.IsGenericMethod)
        {
            return result;
        }

        var arguments = new ITypeSymbol[method.TypeArguments.Length];
        var changed = !ReferenceEquals(result, method);
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = Substitute(method.TypeArguments[i]);
            changed |= !ReferenceEquals(arguments[i], method.TypeArguments[i]);
        }

        return changed ? result.ConstructedFrom.Construct(arguments) : method;
    }

    private ITypeSymbol SubstituteNamed(INamedTypeSymbol named)
    {
        var containing = named.ContainingType is { } outer ? Substitute(outer) as INamedTypeSymbol : null;
        var changed = containing is not null && !ReferenceEquals(containing, named.ContainingType);
        var arguments = new ITypeSymbol[named.TypeArguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = Substitute(named.TypeArguments[i]);
            changed |= !ReferenceEquals(arguments[i], named.TypeArguments[i]);
        }

        if (!changed)
        {
            return named;
        }

        var definition = named.OriginalDefinition;
        if (containing is not null && !ReferenceEquals(containing, named.ContainingType))
        {
            definition = containing.GetTypeMembers(named.Name, named.Arity)
                .AsStackEnumerable()
                .FirstOrDefault(named.OriginalDefinition, static (t, d) => t.OriginalDefinition.IsEqualTo(d)) ?? definition;
        }

        return arguments.Length == 0 ? definition : definition.Construct(arguments);
    }

    private static ISymbol? FindMember(INamedTypeSymbol type, ISymbol member) =>
        type.GetMembers(member.Name)
            .AsStackEnumerable()
            .FirstOrDefault(member.OriginalDefinition, static (m, d) => m.OriginalDefinition.IsEqualTo(d));

    // Outermost containing type first, the order of GenericInstance.TypeArguments
    private static ImmutableArray<ITypeParameterSymbol> CollectTypeParameters(IMethodSymbol definition)
    {
        var levels = new Stack<ImmutableArray<ITypeParameterSymbol>>();
        levels.Push(definition.TypeParameters);
        for (var type = definition.ContainingType; type != null; type = type.ContainingType)
        {
            levels.Push(type.OriginalDefinition.TypeParameters);
        }

        var builder = ImmutableArray.CreateBuilder<ITypeParameterSymbol>();
        while (levels.Count > 0)
        {
            builder.AddRange(levels.Pop());
        }

        return builder.ToImmutable();
    }
}
