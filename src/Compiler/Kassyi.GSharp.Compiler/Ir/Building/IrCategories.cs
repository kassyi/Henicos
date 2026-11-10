using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Building;

/// <summary>
/// The value category that a node gives each expression it holds: whether it reads the value or uses the location.
/// </summary>
/// <remarks>
/// Whether an expression is a variable is C#'s classification, read through <see
/// cref="OperationExtensions.IsVariable(IOperation?)"/>
/// so that the IR and the analyses share one rule.
/// </remarks>
internal static class IrCategories
{
    /// <summary>A value that is read: <see cref="IrCategory.Pointer"/> for a pointer type.</summary>
    public static IrCategory ForRead(ITypeSymbol? type) =>
        type is IPointerTypeSymbol ? IrCategory.Pointer : IrCategory.Value;

    /// <summary>The location of a reference of the given kind (a ref local, a ref return, a ref assignment).</summary>
    public static IrCategory ForReference(RefKind refKind) =>
        refKind is RefKind.In or RefKind.RefReadOnlyParameter ? IrCategory.ReadOnlyLocation : IrCategory.Location;

    /// <summary>
    /// The argument of <paramref name="parameter"/>: a location for <see langword="ref"/> and <see langword="out"/>;
    /// for
    /// <see langword="in"/> and <see langword="ref readonly"/> a read-only location when the argument is a variable,
    /// otherwise the value that C# copies into a hidden temporary.
    /// </summary>
    public static IrCategory ForArgument(IParameterSymbol parameter, IOperation value) => parameter.RefKind switch
    {
        RefKind.Ref or RefKind.Out => IrCategory.Location,
        RefKind.In or RefKind.RefReadOnlyParameter when value.IsVariable() => IrCategory.ReadOnlyLocation,
        _ => ForRead(value.Type)
    };

    /// <summary>
    /// The receiver of an instance method or property accessor: the value of a reference type or of a ref struct (a
    /// span is a view, so a copy of it is the same span); for any other struct the location (read-only for a
    /// <see langword="readonly"/> member) when it is a variable, otherwise the value that C# copies.
    /// </summary>
    public static IrCategory ForReceiver(IOperation receiver, IMethodSymbol? accessor)
    {
        if (receiver.Type is not { IsValueType: true, IsRefLikeType: false } || !receiver.IsVariable())
        {
            return ForRead(receiver.Type);
        }

        return accessor is { IsReadOnly: true } ? IrCategory.ReadOnlyLocation : IrCategory.Location;
    }

    /// <summary>
    /// The receiver of a field: the value of a reference type; for a struct variable the category of the field itself
    /// when that is a location, since the field is part of the receiver's storage.
    /// </summary>
    public static IrCategory ForFieldReceiver(IOperation receiver, IrCategory fieldCategory)
    {
        if (receiver.Type is not { IsValueType: true } || !receiver.IsVariable())
        {
            return ForRead(receiver.Type);
        }

        return fieldCategory is IrCategory.Location or IrCategory.ReadOnlyLocation ? fieldCategory : IrCategory.Value;
    }

    /// <summary>The accessor of a property that the use calls: the setter when the property is written.</summary>
    public static IMethodSymbol? AccessorOf(IPropertySymbol property, IrCategory category) =>
        category == IrCategory.Location && property.RefKind == RefKind.None ? property.SetMethod : property.GetMethod;
}
