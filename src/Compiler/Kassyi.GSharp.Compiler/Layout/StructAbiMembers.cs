namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>The kind of a <see cref="StructAbiMember"/>.</summary>
public enum StructAbiMemberKind
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
    ComputedProperty,

    /// <summary>
    /// The hidden field (<c>&lt;a&gt;P</c>) that Roslyn declares for a primary constructor parameter that members read,
    /// named after the parameter.
    /// </summary>
    CapturedParameter,

    /// <summary>
    /// The hidden field that stores the delegate of a field-like event (<c>event Action E;</c>), listed as the event
    /// (<see cref="StructAbiMember.Symbol"/>), under its name and with the delegate type.
    /// </summary>
    /// <remarks>
    /// [WHY] Roslyn declares the field (<c>SourceEventFieldSymbol</c>, implicitly declared, the event as its
    /// <c>AssociatedSymbol</c>) but does not list it in <c>GetMembers()</c>, neither for a struct of the compilation nor
    /// for one from metadata, so it is found through the event (<see cref="StructAbiMembers.IsFieldLikeEvent"/>).
    /// </remarks>
    EventBackingField,

    /// <summary>
    /// A compiler-generated field in <c>GetMembers()</c> that none of the other kinds explains. It has storage, so it is
    /// listed rather than dropped, and the layout reports the struct as one the device cannot lay out (GSH0078).
    /// </summary>
    UnknownHiddenField,

    /// <summary>
    /// A property whose accessors use the <c>field</c> keyword (<c>int X { get; set =&gt; field = value * 2; }</c>),
    /// found through its backing field and named after the property. The backing field is stored under a name of its
    /// own, and the accessors are member functions, as for a <see cref="ComputedProperty"/>.
    /// </summary>
    FieldBackedProperty
}

/// <summary>One member of a user struct's native layout.</summary>
/// <param name="Symbol">
/// The symbol that names the member: the field, or the property for an auto-property or a property that uses the
/// <c>field</c> keyword.
/// </param>
/// <param name="Name">
/// The declared name; an auto-property keeps the name of the property, a captured primary constructor parameter the
/// name of the parameter.
/// </param>
/// <param name="Type">The type of one element: the pointed-at type of a <c>fixed</c> buffer, the field type otherwise.</param>
/// <param name="ElementCount">The number of elements: 1, or N for a <c>fixed</c> buffer or the element of an <c>[InlineArray(N)]</c> struct.</param>
/// <param name="Kind">What the member was declared as.</param>
/// <param name="StorageField">
/// The field that holds the member: the field itself, or the backing field of an auto-property or of a property that
/// uses the <c>field</c> keyword; null for a computed
/// property and for a field-like event (Roslyn does not list its field). Read the member's offset from it
/// (<see cref="StructLayoutReader.GetFieldOffset"/>) instead of searching for it again.
/// </param>
public readonly record struct StructAbiMember(
    ISymbol Symbol,
    string Name,
    ITypeSymbol Type,
    int ElementCount,
    StructAbiMemberKind Kind,
    IFieldSymbol? StorageField);

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
    /// uses the <c>field</c> keyword (<c>int X { get; set =&gt; field = value * 2; }</c>): the device stores the backing
    /// field and calls the accessors as member functions.
    /// </summary>
    public static bool IsFieldBackedProperty(IPropertySymbol property) =>
        property is { IsStatic: false, IsIndexer: false } &&
        HasBackingField(property.ContainingType.GetMembers(), property) &&
        !AreAccessorsAutoImplemented(property);

    /// <summary>Whether every accessor of <paramref name="property"/> is implemented by the C# compiler.</summary>
    /// <remarks>
    /// In source, an accessor without a body (<c>get;</c>) or the property of a positional record parameter; a
    /// <c>partial</c> property always has bodies, on its
    /// implementing part. From metadata, an accessor that the C# compiler marks <c>[CompilerGenerated]</c>, which it
    /// does for an implemented accessor only. Anything else counts as written by hand: treating an auto-property as one
    /// whose accessors use <c>field</c> gives the same result (its accessors read and write the backing field), while the
    /// opposite would drop the accessors.
    /// </remarks>
    internal static bool AreAccessorsAutoImplemented(IPropertySymbol property)
    {
        if (property.DeclaringSyntaxReferences.IsEmpty)
        {
            return IsCompilerGenerated(property.GetMethod) && IsCompilerGenerated(property.SetMethod);
        }

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax();

            // The property of a positional record parameter, which the C# compiler implements
            if (syntax is ParameterSyntax)
            {
                continue;
            }

            if (syntax is not PropertyDeclarationSyntax declaration ||
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword) ||
                declaration.ExpressionBody != null ||
                declaration.AccessorList is not { } accessorList ||
                accessorList.Accessors.AsStackEnumerable().Any(static a => a.Body != null || a.ExpressionBody != null))
            {
                return false;
            }
        }

        return true;
    }

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

    private static bool HasBackingField(ImmutableArray<ISymbol> members, IPropertySymbol property) =>
        members
            .AsStackEnumerable()
            .Any(property, static (member, p) => member is IFieldSymbol field && IsBackingFieldOf(field, p));

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

        private bool TryCreate(IFieldSymbol field, out StructAbiMember member)
        {
            member = default;
            if (TryGetBackingFieldProperty(field, out var property))
            {
                var kind = AreAccessorsAutoImplemented(property)
                    ? StructAbiMemberKind.AutoProperty
                    : StructAbiMemberKind.FieldBackedProperty;
                member = new StructAbiMember(property, property.Name, field.Type, 1, kind, field);
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
