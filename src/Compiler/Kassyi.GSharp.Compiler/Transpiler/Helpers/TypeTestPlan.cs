namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// What device code writes for a type test or <c>as</c>. Everything the emitter needs is decided here: it writes the
/// plan and makes no choice of its own.
/// </summary>
/// <param name="Kind">How the test is written.</param>
/// <param name="Value">The constant of <see cref="TypeTestKind.Constant"/> (already negated for <c>is not</c>).</param>
/// <param name="Negated">Whether a <see cref="TypeTestKind.NotNull"/> test is the opposite one (<c>is not</c>).</param>
/// <param name="OperandIsNullable">
/// Whether the operand is a <c>Nullable&lt;T&gt;</c> (tested by <c>HasValue</c>) rather than an array (tested by its
/// <c>data</c> pointer).
/// </param>
/// <param name="AssignsVariable">Whether a declaration pattern assigns its variable where the operand passes.</param>
/// <param name="UnwrapsNullable">Whether the variable gets the value of a nullable operand (<c>.Value</c>).</param>
internal readonly record struct TypeTestPlan(
    TypeTestKind Kind,
    bool Value = false,
    bool Negated = false,
    bool OperandIsNullable = false,
    bool AssignsVariable = false,
    bool UnwrapsNullable = false);
