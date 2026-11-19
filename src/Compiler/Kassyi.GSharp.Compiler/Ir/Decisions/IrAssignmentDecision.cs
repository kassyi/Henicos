namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Decisions for an <see cref="Expressions.IrAssignment"/> node (#1561).</summary>
/// <param name="store">
/// The element store plan when the target is an array element; otherwise <see langword="null"/>.
/// </param>
internal sealed class IrAssignmentDecision(IrElementStore? store) : IrDecision
{
    /// <summary>Shared decision for assignments that are not array-element stores.</summary>
    public static IrAssignmentDecision None { get; } = new(null);

    /// <summary>The element store plan when the target is an array element; otherwise <see langword="null"/>.</summary>
    public IrElementStore? Store { get; } = store;
}
