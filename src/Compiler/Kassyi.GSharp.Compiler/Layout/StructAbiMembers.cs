namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>The kind of a <see cref="StructAbiMember"/>.</summary>
internal enum StructAbiMemberKind
{
    /// <summary>An ordinary instance field.</summary>
    Field,

    /// <summary>An auto-property, found through its backing field and named after the property.</summary>
    AutoProperty,

    /// <summary>A <c>fixed</c> buffer: <c>T name[N]</c>.</summary>
    FixedBuffer,

    /// <summary>The one field of an <c>[InlineArray(N)]</c> struct: <c>T element[N]</c>.</summary>
    InlineArrayElement,

    /// <summary>A property without a backing field. Listed only when asked for (see <see cref="StructAbiMembers.Get"/>).</summary>
    ComputedProperty
}

/// <summary>One member of a user struct's native layout.</summary>
/// <param name="Symbol">The symbol that names the member: the field, or the property for an auto-property.</param>
/// <param name="Name">The declared name; an auto-property keeps the name of the property.</param>
/// <param name="Type">The type of one element: the pointed-at type of a <c>fixed</c> buffer, the field type otherwise.</param>
/// <param name="ElementCount">The number of elements: 1, or N for a <c>fixed</c> buffer or the element of an <c>[InlineArray(N)]</c> struct.</param>
/// <param name="Kind">What the member was declared as.</param>
internal readonly record struct StructAbiMember(
    ISymbol Symbol,
    string Name,
    ITypeSymbol Type,
    int ElementCount,
    StructAbiMemberKind Kind);

/// <summary>
/// The members that make up the native layout of a user struct. The device definition (<c>StructTranspiler</c>), the
/// host shadow struct (<c>NativeStructEmitter</c>), its marshaling (<c>StructMarshalingEmitter</c>) and the layout
/// computation (<see cref="CudaLayoutRules"/>) walk this one set so that both sides agree on the offsets.
/// </summary>
/// <remarks>
/// The set holds instance fields, auto-properties (through the backing field, under the property name), <c>fixed</c>
/// buffers and the element of an <c>[InlineArray(N)]</c> struct, in declaration order, each with its element count.
/// The layout, the shadow struct and the marshaling size a member as element layout times
/// <see cref="StructAbiMember.ElementCount"/> and copy an <c>[InlineArray(N)]</c> struct or a <c>fixed</c> buffer as one
/// block. A property without a backing field has no storage on either side and is listed only when
/// <c>includeComputedProperties</c> is set; the device definition emits a <see cref="IsComputedProperty">computed
/// property</see>'s accessors as member functions instead (pinned by <c>StructAbiTests</c>).
/// </remarks>
internal static class StructAbiMembers
{
    /// <summary>The members of <paramref name="structType"/> in declaration order.</summary>
    /// <param name="structType">The struct.</param>
    /// <param name="includeComputedProperties">Also list the properties that have no backing field (the device emits them as getters).</param>
    public static MemberSet Get(INamedTypeSymbol structType, bool includeComputedProperties = false) =>
        new(structType, includeComputedProperties);

    /// <summary>
    /// Whether <paramref name="property"/> is a computed property: an instance property without a backing field, whose
    /// <c>get</c> and <c>set</c> / <c>init</c> accessors are emitted as member functions of the struct.
    /// </summary>
    public static bool IsComputedProperty(IPropertySymbol property) =>
        property is { IsStatic: false, IsIndexer: false, IsImplicitlyDeclared: false } &&
        !HasBackingField(property.ContainingType.GetMembers(), property);

    /// <summary>Whether <paramref name="property"/> is an auto-property, which the device stores as a field.</summary>
    public static bool IsAutoProperty(IPropertySymbol property) =>
        HasBackingField(property.ContainingType.GetMembers(), property);

    private static bool HasBackingField(ImmutableArray<ISymbol> members, IPropertySymbol property) =>
        members
            .AsStackEnumerable()
            .Any(property, static (member, p) => member is IFieldSymbol field && field.AssociatedSymbol.IsEqualTo(p));

    /// <summary>A <c>foreach</c>-able view of the members that allocates nothing.</summary>
    internal readonly struct MemberSet(INamedTypeSymbol structType, bool includeComputedProperties)
    {
        public MemberEnumerator GetEnumerator() => new(structType, includeComputedProperties);
    }

    internal struct MemberEnumerator
    {
        private readonly ImmutableArray<ISymbol> _members;
        private readonly bool _includeComputedProperties;
        private readonly int _inlineArrayLength;
        private int _index = -1;
        private bool _inlineElementTaken;

        public MemberEnumerator(INamedTypeSymbol structType, bool includeComputedProperties)
        {
            _members = structType.GetMembers();
            _includeComputedProperties = includeComputedProperties;
            _inlineArrayLength = structType.GetAttribute(KnownAttributes.InlineArray) is { ConstructorArguments.Length: > 0 } attribute
                ? (int)(attribute.ConstructorArguments[0].Value ?? 0)
                : 0;
        }

        public StructAbiMember Current { get; private set; }

        public bool MoveNext()
        {
            while (++_index < _members.Length)
            {
                var member = _members[_index];
                if (member is IFieldSymbol { IsStatic: false } field)
                {
                    if (TryCreate(field, out var created))
                    {
                        Current = created;
                        return true;
                    }
                }
                else if (_includeComputedProperties && member is IPropertySymbol { IsStatic: false, IsImplicitlyDeclared: false } property &&
                         !HasBackingField(_members, property))
                {
                    Current = new StructAbiMember(property, property.Name, property.Type, 1,
                        StructAbiMemberKind.ComputedProperty);
                    return true;
                }
            }

            return false;
        }

        private bool TryCreate(IFieldSymbol field, out StructAbiMember member)
        {
            member = default;
            if (field.AssociatedSymbol is IPropertySymbol property)
            {
                member = new StructAbiMember(property, property.Name, field.Type, 1, StructAbiMemberKind.AutoProperty);
                return true;
            }

            if (field.IsImplicitlyDeclared)
            {
                return false;
            }

            if (field is { IsFixedSizeBuffer: true, Type: IPointerTypeSymbol pointer })
            {
                member = new StructAbiMember(field, field.Name, pointer.PointedAtType, field.FixedSize,
                    StructAbiMemberKind.FixedBuffer);
                return true;
            }

            if (_inlineArrayLength > 0 && !_inlineElementTaken)
            {
                _inlineElementTaken = true;
                member = new StructAbiMember(field, field.Name, field.Type, _inlineArrayLength,
                    StructAbiMemberKind.InlineArrayElement);
                return true;
            }

            member = new StructAbiMember(field, field.Name, field.Type, 1, StructAbiMemberKind.Field);
            return true;
        }
    }
}
