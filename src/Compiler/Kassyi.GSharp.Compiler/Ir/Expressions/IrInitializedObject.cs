namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>
/// The value that an object initializer is building: the receiver of the members it assigns
/// (<see cref="IrStructCreation.Initializers"/>).
/// </summary>
/// <param name="type">The type of the value.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrInitializedObject(ITypeSymbol type, IrSource source)
    : IrExpression(type, IrCategory.Location, default, source);
