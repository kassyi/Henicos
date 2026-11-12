namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The check of a <c>Gpu.NotNull</c> call
/// (<see cref="NotNullCallCheck.Plan(InvocationExpressionSyntax, ExpressionSyntax, ref TranspileContext)"/>): the array
/// argument, and the place a null reports, or <see langword="null"/> when the array is proven and the call is the array
/// itself.
/// </summary>
internal readonly record struct NotNullCheck(ExpressionSyntax Array, NullCheckSite? Site);
