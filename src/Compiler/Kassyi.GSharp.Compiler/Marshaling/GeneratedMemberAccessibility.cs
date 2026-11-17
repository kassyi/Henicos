namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// The accessibility of a generated member: the widest declared accessibility under which the member, declared in a
/// given type, names only types at least as accessible as itself (CS0050 to CS0053).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NamedTypes"/> collects the types a generated member names and defers checking them until
/// <see cref="NamedTypes.ChooseIn"/> receives the containing type. It tests the declared accessibilities in order from
/// widest to narrowest (<c>public</c>, <c>protected internal</c>, <c>internal</c>, <c>protected</c>,
/// <c>private protected</c>, <c>private</c>) using <see cref="AccessibilityConsistency"/>, skipping protected
/// accessibilities on <c>struct</c>, <c>static</c>, and <c>sealed</c> containers (which would cause CS0666, CS1057,
/// or CS0628).
/// </para>
/// <para>
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
    /// <paramref name="type"/>: it is public, internal or protected internal, as are the types around it and its type
    /// arguments.
    /// </summary>
    public static bool IsAtLeastInternal(ITypeSymbol type) =>
        AccessibilityConsistency.IsAtLeastInternal(type);

    /// <summary>
    /// The greatest lower bound (meet) of two accessibilities of members of the same type in the accessibility lattice
    /// (<c>public</c> &gt; <c>protected internal</c> &gt; {<c>internal</c>, <c>protected</c>} &gt;
    /// <c>private protected</c> &gt; <c>private</c>).
    /// </summary>
    public static Accessibility Narrowest(Accessibility first, Accessibility second) =>
        MaskToAccessibility(ToMask(first) & ToMask(second));

    /// <summary>
    /// The C# keyword(s) of <paramref name="accessibility"/>: <c>public</c>, <c>protected internal</c>,
    /// <c>internal</c>, <c>protected</c>, <c>private protected</c> or <c>private</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for an accessibility that <see cref="NamedTypes.ChooseIn"/> does not return.
    /// </exception>
    public static string ToKeyword(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.Private => "private",
        _ => throw new ArgumentOutOfRangeException(nameof(accessibility), accessibility,
            "A generated member has a declared accessibility.")
    };

    // 4-bit accessibility domain: World (8) | Assembly (4) | FamilyOutsideAssembly (2) | FamilyInAssembly (1)
    private static int ToMask(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => 0b1111,
        Accessibility.ProtectedOrInternal => 0b0111,
        Accessibility.Internal => 0b0101,
        Accessibility.Protected => 0b0011,
        Accessibility.ProtectedAndInternal => 0b0001,
        _ => 0b0000
    };

    private static Accessibility MaskToAccessibility(int mask) => mask switch
    {
        0b1111 => Accessibility.Public,
        0b0111 => Accessibility.ProtectedOrInternal,
        0b0101 => Accessibility.Internal,
        0b0011 => Accessibility.Protected,
        0b0001 => Accessibility.ProtectedAndInternal,
        _ => Accessibility.Private
    };

    /// <summary>
    /// An immutable collection of types and generated nested types that a generated member names;
    /// <see langword="default"/> names none.
    /// </summary>
    internal readonly struct NamedTypes
    {
        private readonly Node? _head;

        private NamedTypes(Node? head) => _head = head;

        /// <summary>These types and <paramref name="type"/>.</summary>
        public NamedTypes With(ITypeSymbol type) =>
            new(new Node(type, null, Accessibility.NotApplicable, false, _head));

        /// <summary>
        /// These types and <paramref name="type"/>, named by an <c>internal</c> member (a field of a frame): only a
        /// type that part of the assembly cannot name narrows them.
        /// </summary>
        public NamedTypes WithInternalMember(ITypeSymbol type) =>
            new(new Node(type, null, Accessibility.NotApplicable, true, _head));

        /// <summary>
        /// These types and a type that G# generates into <paramref name="container"/> with
        /// <paramref name="accessibility"/>, such as the frame of a device function, named by an <c>internal</c> member
        /// (<see cref="WithInternalMember"/>).
        /// </summary>
        public NamedTypes WithGenerated(INamedTypeSymbol container, Accessibility accessibility) =>
            new(new Node(null, container, accessibility, true, _head));

        /// <summary>
        /// The widest accessibility under which a member declared in <paramref name="container"/> can name these
        /// types; <see cref="Accessibility.Private"/> when no wider candidate is allowed.
        /// </summary>
        public Accessibility ChooseIn(INamedTypeSymbol container)
        {
            if (_head is null || AllowsAll(Accessibility.Public, container))
            {
                return Accessibility.Public;
            }

            var allowsProtected = !container.IsStatic && !container.IsSealed && container.TypeKind != TypeKind.Struct;
            if (allowsProtected && AllowsAll(Accessibility.ProtectedOrInternal, container))
            {
                return Accessibility.ProtectedOrInternal;
            }

            if (AllowsAll(Accessibility.Internal, container))
            {
                return Accessibility.Internal;
            }

            if (allowsProtected && AllowsAll(Accessibility.Protected, container))
            {
                return Accessibility.Protected;
            }

            if (allowsProtected && AllowsAll(Accessibility.ProtectedAndInternal, container))
            {
                return Accessibility.ProtectedAndInternal;
            }

            return Accessibility.Private;
        }

        private bool AllowsAll(Accessibility candidate, INamedTypeSymbol container)
        {
            for (var node = _head; node is not null; node = node.Next)
            {
                var allowed = node.Type is { } type
                    ? AccessibilityConsistency.IsAtLeastAsVisibleAs(
                        type, candidate, container, node.IsInternalMember)
                    : node.GeneratedContainer is { } generatedContainer &&
                      AccessibilityConsistency.IsGeneratedAtLeastAsVisibleAs(
                          generatedContainer, node.GeneratedAccessibility, candidate, container, node.IsInternalMember);
                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class Node(
            ITypeSymbol? type,
            INamedTypeSymbol? generatedContainer,
            Accessibility generatedAccessibility,
            bool isInternalMember,
            Node? next)
        {
            public ITypeSymbol? Type { get; } = type;

            public INamedTypeSymbol? GeneratedContainer { get; } = generatedContainer;

            public Accessibility GeneratedAccessibility { get; } = generatedAccessibility;

            public bool IsInternalMember { get; } = isInternalMember;

            public Node? Next { get; } = next;
        }
    }
}
