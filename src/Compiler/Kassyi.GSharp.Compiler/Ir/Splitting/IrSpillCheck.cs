namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// The location <see cref="IrSpillStep.Node"/> is evaluated once for its checks (a null array, an index out of range)
/// before the split, as C# selects it there; its value is not used.
/// </summary>
/// <remarks>
/// C# checks an array element that is the target of a simple assignment when it stores the value, after the value is
/// evaluated; every other element it selects, and every span element, is checked where it is selected (Roslyn's
/// <c>SpillSequenceSpiller</c> evaluates such a receiver once for its exceptions before the split).
/// </remarks>
/// <param name="Node">The element of an array or of a span.</param>
internal sealed record IrSpillCheck(IrExpression Node) : IrSpillStep(Node);
