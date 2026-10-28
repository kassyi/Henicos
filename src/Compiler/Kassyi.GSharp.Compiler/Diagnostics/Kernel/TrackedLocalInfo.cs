namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>A local that must not be live across a split point (<see cref="KernelBarrierValidator"/>) and why.</summary>
internal readonly struct TrackedLocalInfo(
    VariableDeclaratorSyntax declarator,
    ILocalSymbol symbol,
    bool isRefLikeOrRef,
    bool isLocalPointer,
    bool isIteratorLocal)
{
    public readonly VariableDeclaratorSyntax Declarator = declarator;
    public readonly ILocalSymbol Symbol = symbol;
    public readonly string IdentifierText = declarator.Identifier.ValueText;
    public readonly bool IsRefLikeOrRef = isRefLikeOrRef;
    public readonly bool IsLocalPointer = isLocalPointer;
    public readonly bool IsIteratorLocal = isIteratorLocal;
}
