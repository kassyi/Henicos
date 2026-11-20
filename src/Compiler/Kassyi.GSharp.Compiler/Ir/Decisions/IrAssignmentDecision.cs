namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Decisions for an <see cref="Expressions.IrAssignment"/> node (#1561). <see cref="Accessor"/> is a native-dialect
/// decision and is not read by managed writers.
/// </summary>
/// <param name="store">
/// The element store plan when the target is an array element; otherwise <see langword="null"/>.
/// </param>
/// <param name="accessor">How a simple assignment to a property target is written.</param>
internal sealed class IrAssignmentDecision(IrElementStore? store, IrAccessorStore accessor) : IrDecision
{
    /// <summary>Shared decision for assignments that are neither array-element stores nor accessor stores.</summary>
    public static IrAssignmentDecision None { get; } = new(null, IrAccessorStore.None);

    /// <summary>The element store plan when the target is an array element; otherwise <see langword="null"/>.</summary>
    public IrElementStore? Store { get; } = store;

    /// <summary>
    /// How a simple assignment to a property target is written by the native dialect;
    /// <see cref="IrAccessorStore.None"/> for any other target and for a ref assignment.
    /// </summary>
    public IrAccessorStore Accessor { get; } = accessor;
}
