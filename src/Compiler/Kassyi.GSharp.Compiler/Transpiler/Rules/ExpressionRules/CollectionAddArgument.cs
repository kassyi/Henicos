namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// One argument of the <c>Add</c> call of a collection initializer element (<see cref="CollectionAddPlan"/>).
/// </summary>
/// <param name="Value">The expression of the argument, of the tree being transpiled (a lowered tree too).</param>
/// <param name="ParameterType">The type of the parameter, which the temporary of the argument is declared with.</param>
/// <param name="ByReference">
/// Whether the parameter is <c>ref</c>, <c>in</c> or <c>out</c>, so that the temporary is passed with <c>&amp;</c>.
/// </param>
/// <param name="Conversion">
/// The user-defined implicit conversion C# applies to the argument, when lowering replaced the element with a
/// temporary that no longer carries it; null when the argument converts itself.
/// </param>
internal readonly record struct CollectionAddArgument(
    ExpressionSyntax Value,
    ITypeSymbol ParameterType,
    bool ByReference,
    IMethodSymbol? Conversion);
