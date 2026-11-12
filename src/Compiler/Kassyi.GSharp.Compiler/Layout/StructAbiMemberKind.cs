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
