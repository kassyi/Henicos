namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// The members that make up the native layout of a user struct. The device definition (<c>StructTranspiler</c>), the
/// host shadow struct (<c>NativeStructEmitter</c>), its marshaling (<c>StructMarshalingEmitter</c>) and the layout
/// computation (<see cref="TypeLayoutCalculator"/>) walk this one set so that both sides agree on the offsets.
/// </summary>
/// <remarks>
/// The set holds instance fields, auto-properties (through the backing field, under the property name), <c>fixed</c>
/// buffers, the element of an <c>[InlineArray(N)]</c> struct, the hidden fields of captured primary constructor
/// parameters (under the parameter name) and of field-like events (under the event name), in the order of
/// <c>GetMembers()</c>, each with its element count. Every instance field that the C# compiler emits is listed (pinned by
/// <c>StructAbiMembersTests</c> against the emitted metadata). That is
/// the order in which the C# compiler emits the fields, so it is the host layout of a sequential struct: Roslyn lists
/// the hidden fields of the captured parameters first.
/// The layout, the shadow struct and the marshaling size a member as element layout times
/// <see cref="StructAbiMember.ElementCount"/> and copy a <c>fixed</c> buffer as one block; an <c>[InlineArray(N)]</c>
/// struct is one block when its element lays out the same on both sides, and is copied element by element otherwise.
/// A property without a backing field has no storage on either side and is listed only when
/// <c>includeComputedProperties</c> is set; the device definition emits a <see cref="HasAccessorFunctions">computed
/// property</see>'s accessors as member functions instead (pinned by <c>StructAbiTests</c>).
/// </remarks>
internal static class StructAbiMembers
{
    private const string BackingFieldSuffix = ">k__BackingField";

    /// <summary>The members of <paramref name="structType"/> in declaration order.</summary>
    /// <param name="structType">The struct.</param>
    /// <param name="includeComputedProperties">Also list the properties that have no backing field (the device emits them as getters).</param>
    public static MemberSet Get(INamedTypeSymbol structType, bool includeComputedProperties = false) =>
        new(structType, includeComputedProperties);

    /// <summary>
    /// Whether the <c>get</c> and <c>set</c> / <c>init</c> accessors of <paramref name="property"/> are emitted as
    /// member functions of the struct: an instance property without a backing field (a computed property), or one whose
    /// accessors use the <c>field</c> keyword (<see cref="IsFieldBackedProperty"/>).
    /// </summary>
    public static bool HasAccessorFunctions(IPropertySymbol property) =>
        property is { IsStatic: false, IsIndexer: false, IsImplicitlyDeclared: false } &&
        (!HasBackingField(property.ContainingType.GetMembers(), property) || !AreAccessorsAutoImplemented(property));

    /// <summary>
    /// Whether <paramref name="property"/> is an auto-property: a backing field and only accessors that the C# compiler
    /// implements (<c>{ get; set; }</c>). The device stores it as a field and reads and writes that field directly.
    /// </summary>
    public static bool IsAutoProperty(IPropertySymbol property) =>
        HasBackingField(property.ContainingType.GetMembers(), property) && AreAccessorsAutoImplemented(property);

    /// <summary>
    /// Whether <paramref name="property"/> is an instance property with a backing field and an accessor of its own that
    /// uses the <c>field</c> keyword (<c>int X { get; set =&gt; field = value * 2; }</c>): the device stores the
    /// backing
    /// field and calls the accessors as member functions.
    /// </summary>
    public static bool IsFieldBackedProperty(IPropertySymbol property) =>
        property is { IsStatic: false, IsIndexer: false } &&
        HasBackingField(property.ContainingType.GetMembers(), property) &&
        !AreAccessorsAutoImplemented(property);

    /// <summary>Whether every accessor of <paramref name="property"/> is implemented by the C# compiler.</summary>
    /// <remarks>
    /// In source, an accessor without a body (<c>get;</c>) or the property of a positional record parameter; a
    /// <c>partial</c> property always has bodies, on its implementing part. From metadata, an accessor that the C#
    /// compiler marks <c>[CompilerGenerated]</c>, which it does for an implemented accessor only. Anything else counts
    /// as written by hand: treating an auto-property as one whose accessors use <c>field</c> gives the same result (its
    /// accessors read and write the backing field), while the opposite would drop the accessors.
    /// </remarks>
    internal static bool AreAccessorsAutoImplemented(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.IsEmpty
            ? AreMetadataAccessorsGenerated(property)
            : property.DeclaringSyntaxReferences
                .AsStackEnumerable()
                .All(static reference => IsAutoImplementedDeclaration(reference.GetSyntax()));

    private static bool AreMetadataAccessorsGenerated(IPropertySymbol property) =>
        IsCompilerGenerated(property.GetMethod) && IsCompilerGenerated(property.SetMethod);

    // A declaration whose accessors the C# compiler implements: the property of a positional record parameter, or a
    // property that is not partial, has no expression body and no accessor body
    private static bool IsAutoImplementedDeclaration(SyntaxNode syntax) =>
        syntax switch
        {
            ParameterSyntax => true,
            PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } accessorList } declaration =>
                !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                !accessorList.Accessors
                    .AsStackEnumerable()
                    .Any(static a => a.Body != null || a.ExpressionBody != null),
            _ => false
        };

    // An accessor that is missing counts as generated: { get; } has no setter to inspect
    private static bool IsCompilerGenerated(IMethodSymbol? accessor) =>
        accessor is null || accessor.HasAttribute(KnownAttributes.CompilerGenerated);

    /// <summary>
    /// Whether <paramref name="field"/> is the hidden field that Roslyn declares for a primary constructor parameter
    /// that members read, and the name of that parameter.
    /// </summary>
    /// <remarks>
    /// [WHY] Roslyn names the field <c>&lt;name&gt;P</c> (<c>GeneratedNames.MakePrimaryConstructorParameterFieldName</c>)
    /// and lists it in <c>GetMembers()</c> before the declared members, in the order in which it emits the fields; no
    /// public API links it to the parameter. A struct from metadata carries the same name.
    /// </remarks>
    public static bool TryGetCapturedParameterName(IFieldSymbol field, [NotNullWhen(true)] out string? parameterName)
    {
        const string Suffix = ">P";
        var name = field.Name;
        if (field is { IsStatic: false, AssociatedSymbol: null, CanBeReferencedByName: false } &&
            name.Length > Suffix.Length + 1 &&
            name[0] == '<' &&
            name.EndsWith(Suffix, StringComparison.Ordinal))
        {
            parameterName = name[1..(name.Length - Suffix.Length)];
            return true;
        }

        parameterName = null;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="event"/> is a field-like event (<c>event Action E;</c>), which stores its delegate in a
    /// hidden field.
    /// </summary>
    /// <remarks>
    /// In source, it is declared as an event field that is neither <c>extern</c> nor the defining part of a
    /// <c>partial</c> event (whose implementing part writes <c>add</c> and <c>remove</c>). From metadata, the C# compiler
    /// marks its generated accessors <c>[CompilerGenerated]</c>. An event with <c>add</c> and <c>remove</c> written out
    /// stores nothing.
    /// </remarks>
    internal static bool IsFieldLikeEvent(IEventSymbol @event)
    {
        if (@event.IsExtern)
        {
            return false;
        }

        if (@event.DeclaringSyntaxReferences.IsEmpty)
        {
            return @event.AddMethod is { } add && add.HasAttribute(KnownAttributes.CompilerGenerated);
        }

        foreach (var reference in @event.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is VariableDeclaratorSyntax { Parent.Parent: EventFieldDeclarationSyntax declaration } &&
                !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                !declaration.Modifiers.Any(SyntaxKind.ExternKeyword))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The property whose backing field <paramref name="field"/> is, if it is one.</summary>
    /// <remarks>
    /// [WHY] Roslyn links the backing field to its property (<c>AssociatedSymbol</c>) for a struct of the compilation
    /// only; in a struct from metadata the link is null, and the field is found by the name the C# compiler gives it,
    /// <c>&lt;P&gt;k__BackingField</c> (<c>GeneratedNames.MakeBackingFieldName</c>).
    /// </remarks>
    public static bool TryGetBackingFieldProperty(IFieldSymbol field, [NotNullWhen(true)] out IPropertySymbol? property)
    {
        property = field.AssociatedSymbol as IPropertySymbol;
        if (property != null || !IsMetadataBackingField(field))
        {
            return property != null;
        }

        var propertyName = field.Name[1..(field.Name.Length - BackingFieldSuffix.Length)];
        property = field.ContainingType.GetMembers(propertyName)
            .AsStackEnumerable()
            .OfType<IPropertySymbol>()
            .FirstOrDefault();
        return property != null;
    }

    private static bool IsMetadataBackingField(IFieldSymbol field) =>
        field is { AssociatedSymbol: null, CanBeReferencedByName: false, DeclaringSyntaxReferences.IsEmpty: true } &&
        field.Name.Length > BackingFieldSuffix.Length + 1 &&
        field.Name[0] == '<' &&
        field.Name.EndsWith(BackingFieldSuffix, StringComparison.Ordinal);

    // Whether field is the backing field of property, without building the name it would have
    private static bool IsBackingFieldOf(IFieldSymbol field, IPropertySymbol property) =>
        field.AssociatedSymbol.IsEqualTo(property) ||
        (IsMetadataBackingField(field) &&
         field.Name.Length == property.Name.Length + BackingFieldSuffix.Length + 1 &&
         string.CompareOrdinal(field.Name, 1, property.Name, 0, property.Name.Length) == 0);

    /// <summary>
    /// The backing field of <paramref name="property"/>, if it has one, also in a struct from metadata.
    /// </summary>
    public static bool TryGetBackingField(IPropertySymbol property, [NotNullWhen(true)] out IFieldSymbol? field)
    {
        field = FindBackingField(property.ContainingType.GetMembers(), property);
        return field != null;
    }

    private static IFieldSymbol? FindBackingField(ImmutableArray<ISymbol> members, IPropertySymbol property) =>
        members
            .AsStackEnumerable()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(property, static (field, p) => IsBackingFieldOf(field, p));

    private static bool HasBackingField(ImmutableArray<ISymbol> members, IPropertySymbol property) =>
        FindBackingField(members, property) != null;

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
                else if (member is IEventSymbol { IsStatic: false } @event && IsFieldLikeEvent(@event))
                {
                    Current = new StructAbiMember(@event, @event.Name, @event.Type, 1,
                        StructAbiMemberKind.EventBackingField, null);
                    return true;
                }
                else if (_includeComputedProperties && member is IPropertySymbol { IsStatic: false, IsImplicitlyDeclared: false } property &&
                         !HasBackingField(_members, property))
                {
                    Current = new StructAbiMember(property, property.Name, property.Type, 1,
                        StructAbiMemberKind.ComputedProperty, null);
                    return true;
                }
            }

            return false;
        }

        // The backing field of a property, listed as the property: an auto-property, or one that uses the field keyword
        private static bool TryCreatePropertyMember(IFieldSymbol field, out StructAbiMember member)
        {
            member = default;
            if (!TryGetBackingFieldProperty(field, out var property))
            {
                return false;
            }

            var kind = AreAccessorsAutoImplemented(property)
                ? StructAbiMemberKind.AutoProperty
                : StructAbiMemberKind.FieldBackedProperty;
            member = new StructAbiMember(property, property.Name, field.Type, 1, kind, field);
            return true;
        }

        private bool TryCreate(IFieldSymbol field, out StructAbiMember member)
        {
            if (TryCreatePropertyMember(field, out member))
            {
                return true;
            }

            if (TryGetCapturedParameterName(field, out var parameterName))
            {
                member = new StructAbiMember(field, parameterName, field.Type, 1, StructAbiMemberKind.CapturedParameter,
                    field);
                return true;
            }

            if (field.IsImplicitlyDeclared)
            {
                member = new StructAbiMember(field, field.Name, field.Type, 1, StructAbiMemberKind.UnknownHiddenField,
                    field);
                return true;
            }

            if (field is { IsFixedSizeBuffer: true, Type: IPointerTypeSymbol pointer })
            {
                member = new StructAbiMember(field, field.Name, pointer.PointedAtType, field.FixedSize,
                    StructAbiMemberKind.FixedBuffer, field);
                return true;
            }

            if (_inlineArrayLength > 0 && !_inlineElementTaken)
            {
                _inlineElementTaken = true;
                member = new StructAbiMember(field, field.Name, field.Type, _inlineArrayLength,
                    StructAbiMemberKind.InlineArrayElement, field);
                return true;
            }

            member = new StructAbiMember(field, field.Name, field.Type, 1, StructAbiMemberKind.Field, field);
            return true;
        }
    }
}
