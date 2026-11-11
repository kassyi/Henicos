namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A call of a method.</summary>
/// <remarks>
/// An extension method is called as the static method it is: <see cref="Receiver"/> is <see langword="null"/> and the
/// receiver is the first argument, as Roslyn represents it. A call has no constant value in C#.
/// </remarks>
/// <param name="method">The method.</param>
/// <param name="receiver">The receiver of an instance method; <see langword="null"/> for a static method.</param>
/// <param name="arguments">The arguments in parameter order, one per parameter.</param>
/// <param name="classification">What the classify pass decided; unclassified when the builder creates the call.</param>
/// <param name="type">The return type.</param>
/// <param name="category">How the holder uses the result.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrCall(IMethodSymbol method, IrExpression? receiver, ImmutableArray<IrArgument> arguments,
    IrCallClassification classification, ITypeSymbol type, IrCategory category, IrSource source)
    : IrExpression(type, category, default, source)
{
    /// <summary>The method.</summary>
    public IMethodSymbol Method { get; } = method;

    /// <summary>The receiver of an instance method; <see langword="null"/> for a static method.</summary>
    public IrExpression? Receiver { get; } = receiver;

    /// <summary>The arguments in parameter order, one per parameter.</summary>
    public ImmutableArray<IrArgument> Arguments { get; } = arguments;

    /// <summary>What the classify pass decided: whether the call splits and what it may write.</summary>
    public IrCallClassification Classification { get; } = classification;

    /// <summary>
    /// This call with the given children and classification; this node itself when they are the same.
    /// </summary>
    public IrCall Update(IrExpression? receiver, ImmutableArray<IrArgument> arguments,
        IrCallClassification classification) =>
        receiver == Receiver && arguments == Arguments && classification.Equals(Classification)
            ? this
            : new IrCall(Method, receiver, arguments, classification, Type, Category, Source);
}
