using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>The intermediate representation of one method body.</summary>
/// <remarks>Lives within one generator transform or analyzer callback, like the nodes it holds.</remarks>
/// <param name="symbol">The method.</param>
/// <param name="parameters">The parameters, in the order of <see cref="IMethodSymbol.Parameters"/>.</param>
/// <param name="this">The variable of <see langword="this"/> in an instance member.</param>
/// <param name="body">The body.</param>
/// <param name="source">The method declaration.</param>
internal sealed class IrMethod(IMethodSymbol symbol, ImmutableArray<IrVariable> parameters, IrVariable? @this,
    IrBlock body, IrSource source)
{
    /// <summary>The method.</summary>
    public IMethodSymbol Symbol { get; } = symbol;

    /// <summary>The parameters, in the order of <see cref="IMethodSymbol.Parameters"/>.</summary>
    public ImmutableArray<IrVariable> Parameters { get; } = parameters;

    /// <summary>The variable of <see langword="this"/> in an instance member.</summary>
    public IrVariable? This { get; } = @this;

    /// <summary>The body.</summary>
    public IrBlock Body { get; } = body;

    /// <summary>The method declaration.</summary>
    public IrSource Source { get; } = source;
}
