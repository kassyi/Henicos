namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// How a simple assignment to a property is written by the native dialect (<see cref="IrAssignmentDecision"/>, #1561).
/// </summary>
internal enum IrAccessorStore
{
    /// <summary>Not a store through an accessor: an ordinary assignment, or the target is not a property.</summary>
    None,

    /// <summary>The setter of a property with accessor functions is called with the value.</summary>
    CallsSetter,

    /// <summary>
    /// The backing field of a property without a setter whose accessors use the <c>field</c> keyword is written.
    /// </summary>
    WritesBackingField
}
