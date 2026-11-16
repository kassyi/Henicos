namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The emission plan for a <c>break</c> or <c>continue</c> statement (#1990).
/// </summary>
/// <param name="IsBreak">Whether the jump is a <c>break</c> (<see langword="true"/>) or <c>continue</c>.</param>
/// <param name="Form">
/// Whether the jump is written as a plain <c>break;</c> / <c>continue;</c> or as a <c>goto</c>.
/// </param>
/// <param name="Label">The label the jump names, or <see langword="null"/> for a plain jump.</param>
/// <param name="Region">The region of the reference, for the C++ name.</param>
internal readonly record struct LabeledJumpPlan(bool IsBreak, LabeledJumpForm Form, ILabelSymbol? Label, int Region);
