namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// The accessibility of a generated member: the widest of <c>public</c>, <c>internal</c> and <c>private</c> under
/// which the member, declared in a given type, names only types at least as accessible as itself (CS0050 to CS0053).
/// </summary>
/// <remarks>
/// <para>
/// A member's accessibility domain is that of its declared accessibility within its type, intersected with the domain
/// of the type and of each type around it. A named type's domain is the same intersection over the type, the types
/// around it and its type arguments. A domain is represented as everywhere, the assembly, or the program text of one
/// type.
/// </para>
/// <para>
/// A protected domain also reaches derived types, which this representation cannot express. It is widened for the
/// member (protected as everywhere, private protected as the assembly) and narrowed for a type the member names (to the
/// type that declares it), so that the accessibility chosen always compiles, at worst narrower than C# would allow.
/// Members that name only public types stay <c>public</c>, so output that compiled before does not change (#1284).
/// </para>
/// </remarks>
internal static class GeneratedMemberAccessibility
{
    /// <summary>The types of the parameters and of the return value of <paramref name="method"/>.</summary>
    public static NamedTypes OfSignature(IMethodSymbol method)
    {
        var named = default(NamedTypes).With(method.ReturnType);
        foreach (var parameter in method.Parameters)
        {
            named = named.With(parameter.Type);
        }

        return named;
    }

    /// <summary>
    /// The types that the shadow struct and the marshaling methods of <paramref name="type"/> name: those of
    /// <see cref="OfStruct"/> for a user struct, and the <c>Nullable&lt;T&gt;</c> itself, with <c>T</c>, otherwise.
    /// </summary>
    public static NamedTypes OfShadowed(INamedTypeSymbol type) =>
        TypeLayoutCalculator.IsUserStruct(type) ? OfStruct(type) : default(NamedTypes).With(type);

    /// <summary>
    /// <paramref name="structType"/> and the types of its members, which its shadow struct, host mirror and marshaling
    /// methods name.
    /// </summary>
    public static NamedTypes OfStruct(INamedTypeSymbol structType)
    {
        var named = default(NamedTypes).With(structType);
        foreach (var member in StructAbiMembers.Get(structType))
        {
            // GSH0078 reports a member the shadow struct leaves out; nothing generated names its type
            if (!NativeStructEmitter.IsOmittedFromShadow(member))
            {
                named = named.With(member.Type);
            }
        }

        return named;
    }

    /// <summary>
    /// Whether code anywhere in the assembly, and in the assemblies it gives its internals to, can name
    /// <paramref name="type"/>: it is public or internal, as are the types around it and its type arguments.
    /// </summary>
    public static bool IsAtLeastInternal(ITypeSymbol type) =>
        GetTypeDomain(type).Contains(AccessibilityDomain.Assembly);

    /// <summary>The narrower of two accessibilities of members of the same type.</summary>
    public static Accessibility Narrowest(Accessibility first, Accessibility second) =>
        Rank(first) >= Rank(second) ? first : second;

    /// <summary>
    /// The keyword of <paramref name="accessibility"/>: <c>public</c>, <c>internal</c> or <c>private</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for an accessibility that <see cref="NamedTypes.ChooseIn"/> does not return.
    /// </exception>
    public static string ToKeyword(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Private => "private",
        _ => throw new ArgumentOutOfRangeException(nameof(accessibility), accessibility,
            "A generated member is public, internal or private.")
    };

    private static int Rank(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => 0,
        Accessibility.Internal => 1,
        _ => 2
    };

    // The domain of a member with the given accessibility declared in container, widened where protected
    private static AccessibilityDomain GetMemberDomain(Accessibility accessibility, INamedTypeSymbol container)
    {
        var domain = accessibility switch
        {
            Accessibility.Public => AccessibilityDomain.Everywhere,
            Accessibility.Internal => AccessibilityDomain.Assembly,
            _ => AccessibilityDomain.Within(container)
        };
        for (INamedTypeSymbol? type = container; type != null; type = type.ContainingType)
        {
            domain = domain.Intersect(type.DeclaredAccessibility switch
            {
                Accessibility.Internal or Accessibility.ProtectedAndInternal => AccessibilityDomain.Assembly,
                Accessibility.Private when type.ContainingType is { } outer => AccessibilityDomain.Within(outer),
                _ => AccessibilityDomain.Everywhere
            });
        }

        return domain;
    }

    // The domain of a type, narrowed where protected
    private static AccessibilityDomain GetTypeDomain(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return GetTypeDomain(array.ElementType);
            case IPointerTypeSymbol pointer:
                return GetTypeDomain(pointer.PointedAtType);
            case IFunctionPointerTypeSymbol functionPointer:
                return OfSignature(functionPointer.Signature).Domain;
            case INamedTypeSymbol named:
            {
                var domain = AccessibilityDomain.Everywhere;
                for (INamedTypeSymbol? current = named; current != null; current = current.ContainingType)
                {
                    domain = domain.Intersect(GetDeclaredTypeDomain(current));
                    foreach (var argument in current.TypeArguments)
                    {
                        domain = domain.Intersect(GetTypeDomain(argument));
                    }
                }

                return domain;
            }
            default:
                return AccessibilityDomain.Everywhere;
        }
    }

    // A type without a declared accessibility (an error type) narrows nothing
    private static AccessibilityDomain GetDeclaredTypeDomain(INamedTypeSymbol type) => type.DeclaredAccessibility switch
    {
        Accessibility.Public or Accessibility.NotApplicable => AccessibilityDomain.Everywhere,
        Accessibility.Internal or Accessibility.ProtectedOrInternal => AccessibilityDomain.Assembly,
        _ when type.ContainingType is { } outer => AccessibilityDomain.Within(outer),
        _ => AccessibilityDomain.Assembly
    };

    /// <summary>
    /// The types a generated member names, kept as the part of the program from which all of them can be named;
    /// <see langword="default"/> names none.
    /// </summary>
    internal readonly struct NamedTypes
    {
        private NamedTypes(AccessibilityDomain domain) => Domain = domain;

        internal AccessibilityDomain Domain { get; }

        /// <summary>These types and <paramref name="type"/>.</summary>
        public NamedTypes With(ITypeSymbol type) => new(Domain.Intersect(GetTypeDomain(type)));

        /// <summary>
        /// The widest accessibility under which a member declared in <paramref name="container"/> can name these
        /// types; <see cref="Accessibility.Private"/> when none can.
        /// </summary>
        public Accessibility ChooseIn(INamedTypeSymbol container)
        {
            if (Domain.Contains(GetMemberDomain(Accessibility.Public, container)))
            {
                return Accessibility.Public;
            }

            return Domain.Contains(GetMemberDomain(Accessibility.Internal, container))
                ? Accessibility.Internal
                : Accessibility.Private;
        }
    }

    // Everywhere (the default), the assembly, the program text of one type, or nowhere (the intersection of two types
    // that do not nest)
    internal readonly struct AccessibilityDomain
    {
        private readonly DomainKind _kind;
        private readonly INamedTypeSymbol? _type;

        private AccessibilityDomain(DomainKind kind, INamedTypeSymbol? type)
        {
            _kind = kind;
            _type = type;
        }

        public static AccessibilityDomain Everywhere => default;

        public static AccessibilityDomain Assembly => new(DomainKind.Assembly, null);

        public static AccessibilityDomain Within(INamedTypeSymbol type) =>
            new(DomainKind.Within, type.OriginalDefinition);

        private static AccessibilityDomain Nowhere => new(DomainKind.Nowhere, null);

        // Whether every place in other is also in this domain
        public bool Contains(AccessibilityDomain other)
        {
            switch (_kind)
            {
                case DomainKind.Everywhere:
                    return true;
                case DomainKind.Assembly:
                    return other._kind != DomainKind.Everywhere;
                case DomainKind.Within:
                    if (other._kind == DomainKind.Nowhere)
                    {
                        return true;
                    }

                    for (var type = other._type; type != null; type = type.ContainingType)
                    {
                        if (SymbolEqualityComparer.Default.Equals(type, _type))
                        {
                            return true;
                        }
                    }

                    return false;
                default:
                    return other._kind == DomainKind.Nowhere;
            }
        }

        public AccessibilityDomain Intersect(AccessibilityDomain other) =>
            Contains(other) ? other : other.Contains(this) ? this : Nowhere;
    }

    private enum DomainKind
    {
        Everywhere,
        Assembly,
        Within,
        Nowhere
    }
}
