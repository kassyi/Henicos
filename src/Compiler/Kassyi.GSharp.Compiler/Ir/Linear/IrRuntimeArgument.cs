namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>An argument passed to an <see cref="IrRuntimeCall"/>.</summary>
/// <param name="Value">The argument expression.</param>
/// <param name="RefKind">The parameter passing mode from the original intrinsic parameter.</param>
internal readonly record struct IrRuntimeArgument(IrExpression Value, RefKind RefKind);
