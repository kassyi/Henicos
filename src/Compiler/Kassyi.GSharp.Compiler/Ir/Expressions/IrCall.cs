namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A call of a method.</summary>
/// <remarks>
/// An extension method is called as the static method it is: <see cref="Receiver"/> is <see langword="null"/> and the
/// receiver is the first argument, as Roslyn represents it.
/// </remarks>
/// <param name="method">The method.</param>
/// <param name="receiver">The receiver of an instance method; <see langword="null"/> for a static method.</param>
/// <param name="arguments">The arguments in parameter order, one per parameter.</param>
/// <param name="type">The return type.</param>
/// <param name="category">How the holder uses the result.</param>
/// <param name="constantValue">The constant value, as Roslyn gives it.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrCall(IMethodSymbol method, IrExpression? receiver, ImmutableArray<IrArgument> arguments,
    ITypeSymbol type, IrCategory category, Optional<object?> constantValue, IrSource source)
    : IrExpression(type, category, constantValue, source)
{
    /// <summary>The method.</summary>
    public IMethodSymbol Method { get; } = method;

    /// <summary>The receiver of an instance method; <see langword="null"/> for a static method.</summary>
    public IrExpression? Receiver { get; } = receiver;

    /// <summary>The arguments in parameter order, one per parameter.</summary>
    public ImmutableArray<IrArgument> Arguments { get; } = arguments;
}
