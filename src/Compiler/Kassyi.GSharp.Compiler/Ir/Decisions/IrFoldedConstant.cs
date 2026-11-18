namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>A constant that is written as its value, in the type C# gives it where it is used.</summary>
/// <param name="Type">The type C# gives the constant where it is used.</param>
/// <param name="Value">The constant value.</param>
internal readonly record struct IrFoldedConstant(ITypeSymbol Type, object Value);
