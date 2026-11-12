namespace Kassyi.GSharp.Compiler.Layout;

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
