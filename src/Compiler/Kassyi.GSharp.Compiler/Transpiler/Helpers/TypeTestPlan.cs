namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>What device code writes for a type test or <c>as</c>: the kind, and the constant of a constant test.</summary>
/// <param name="Kind">How the test is written.</param>
/// <param name="Value">The constant of <see cref="TypeTestKind.Constant"/>.</param>
internal readonly record struct TypeTestPlan(TypeTestKind Kind, bool Value = false);
