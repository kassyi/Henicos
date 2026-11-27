namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// Identifies symbols declared in the zero-allocation StackLinq namespace (<c>Kassyi.ZeroAlloc.Linq</c>).
/// </summary>
internal static class StackLinqSymbols
{
    /// <summary>Whether <paramref name="symbol"/> is declared in <c>Kassyi.ZeroAlloc.Linq</c>.</summary>
    public static bool IsStackLinqMethod(IMethodSymbol symbol) =>
        symbol.ContainingNamespace is
        {
            Name: "Linq",
            ContainingNamespace: { Name: "ZeroAlloc", ContainingNamespace.Name: "Kassyi" }
        };

    /// <summary>Whether <paramref name="type"/> is declared in <c>Kassyi.ZeroAlloc.Linq</c>.</summary>
    public static bool IsStackLinqType(ITypeSymbol? type) =>
        type?.ContainingNamespace is
        {
            Name: "Linq",
            ContainingNamespace: { Name: "ZeroAlloc", ContainingNamespace.Name: "Kassyi" }
        };
}
