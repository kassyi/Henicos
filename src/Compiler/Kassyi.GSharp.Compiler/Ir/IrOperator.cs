namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>An operator as C# bound it: which one, the user-defined method if any, and the overflow context.</summary>
/// <typeparam name="TKind">The kind of operator: unary, binary, or a conversion.</typeparam>
/// <param name="Kind">Which operator.</param>
/// <param name="Method">The user-defined operator; <see langword="null"/> for a built-in one.</param>
/// <param name="IsChecked">Whether the operator is in a <see langword="checked"/> context.</param>
internal readonly record struct IrOperator<TKind>(TKind Kind, IMethodSymbol? Method, bool IsChecked)
    where TKind : struct, Enum;
