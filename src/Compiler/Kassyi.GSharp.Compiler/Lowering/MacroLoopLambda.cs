namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The body lambda of a <c>Gpu.ForUnroll</c> or <c>Gpu.GridStrideLoop</c> call that
/// <see cref="PreLoweringExpander"/> turns into a loop (<see cref="MacroExpansionSyntax.TryGetLoopLambda"/>).
/// </summary>
/// <param name="Lambda">The body lambda as written.</param>
/// <param name="LoopParameter">The parameter that receives the loop index.</param>
/// <param name="ContextParameter">The context parameter of a two-parameter lambda of a call with a context.</param>
/// <param name="ContextArgument">The context argument that replaces <paramref name="ContextParameter"/>.</param>
internal readonly record struct MacroLoopLambda(
    LambdaExpressionSyntax Lambda,
    ParameterSyntax LoopParameter,
    ParameterSyntax? ContextParameter,
    ExpressionSyntax? ContextArgument);
