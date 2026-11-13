namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The accessor member functions of a struct property whose accessors G# emits
/// (<see cref="StructAbiMembers.HasAccessorFunctions"/>), decided before anything is written
/// (<see cref="StructPropertyEmitter"/> writes them).
/// </summary>
/// <param name="Getter">The getter.</param>
/// <param name="Setter">The setter or <c>init</c> accessor.</param>
/// <param name="ValueParameter">The <c>value</c> parameter of the setter, when it has one.</param>
/// <param name="IsConstGetter">Whether the getter is a <c>const</c> member function
/// (<c>PropertyAccessorCall.IsConstGetter</c>).</param>
internal readonly record struct StructPropertyPlan(
    PropertyAccessorPlan Getter,
    PropertyAccessorPlan Setter,
    IParameterSymbol? ValueParameter,
    bool IsConstGetter);
