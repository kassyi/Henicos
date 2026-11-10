namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A read of, or a reference to, a local, a parameter, <see langword="this"/> or a temporary.</summary>
/// <param name="variable">The variable.</param>
/// <param name="isDeclaration">Whether the reference also declares the local (<c>out var x</c>).</param>
/// <param name="category">How the holder uses the variable.</param>
/// <param name="constantValue">The constant value, as Roslyn gives it.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrVariableReference(IrVariable variable, bool isDeclaration, IrCategory category,
    Optional<object?> constantValue, IrSource source)
    : IrExpression(variable.Type, category, constantValue, source)
{
    /// <summary>The variable.</summary>
    public IrVariable Variable { get; } = variable;

    /// <summary>Whether the reference also declares the local (<c>out var x</c>).</summary>
    public bool IsDeclaration { get; } = isDeclaration;
}
