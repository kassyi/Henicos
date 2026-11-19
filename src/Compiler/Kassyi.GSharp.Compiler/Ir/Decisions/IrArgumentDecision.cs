namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Native-dialect decisions for one argument of an <see cref="Expressions.IrCall"/> (#1561).
/// Both <see cref="ConvertedTo"/> and <see cref="Check"/> are native-dialect decisions and are not read by managed
/// writers.
/// </summary>
/// <param name="ConvertedTo">
/// The primitive parameter type to convert or format the argument as (covering both explicit casts and typed
/// constants), or <see langword="null"/> when no conversion is emitted.
/// </param>
/// <param name="Check">The boundary null check planned for the argument.</param>
internal readonly record struct IrArgumentDecision(ITypeSymbol? ConvertedTo, IrArgumentCheck Check);
