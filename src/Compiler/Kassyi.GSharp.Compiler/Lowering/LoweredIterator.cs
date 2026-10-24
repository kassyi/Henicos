namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// An iterator device function body lowered into a <c>MoveNext()</c> switch-based state machine, with the contents of
/// its struct.
/// </summary>
/// <param name="MoveNextBody">The lowered <c>MoveNext()</c> body.</param>
/// <param name="Fields">
/// Locals hoisted into the iterator struct because they live across a <c>yield</c> boundary.
/// </param>
/// <param name="AssignedParameters">
/// Parameters of the iterator that the body assigns. Each gets a companion <c>__gsharp_initial_&lt;param&gt;</c> field
/// so that <c>GetEnumerator()</c> can start a fresh enumeration from the original argument values.
/// </param>
/// <param name="ElementType">
/// The yielded element type <c>T</c> of the <c>IEnumerable&lt;T&gt;</c> iterator, when lowered with a semantic model.
/// </param>
public sealed record LoweredIterator(
    BlockSyntax MoveNextBody,
    IReadOnlyList<VariableDeclaratorSyntax> Fields,
    IReadOnlyList<string> AssignedParameters,
    ITypeSymbol? ElementType = null);
