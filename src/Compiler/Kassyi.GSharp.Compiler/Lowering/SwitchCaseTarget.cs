namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// A case label of a flattenable switch: its label symbol, its value, its section index and its lowered label.
/// </summary>
/// <param name="Symbol">
/// The label symbol that a <c>goto case</c> binds to, or <see langword="null"/> without a semantic model.
/// </param>
/// <param name="Value">The case value, matched by equivalence when a symbol does not resolve.</param>
/// <param name="SectionIndex">The zero-based index of the section that holds the case.</param>
/// <param name="Label">
/// The lowered label of the section that holds the case, or empty when only section indices are queried.
/// </param>
internal readonly record struct SwitchCaseTarget(
    ILabelSymbol? Symbol,
    ExpressionSyntax Value,
    int SectionIndex,
    string Label);
