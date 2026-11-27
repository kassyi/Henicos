namespace Kassyi.GSharp.Compiler.Ir.Bodies;

/// <summary>
/// The dialect-independent front-end result for one method: its classified IR body and scan facts, both
/// <see langword="null"/> when the builder produced no IR.
/// </summary>
internal sealed class IrBodyFront(IMethodSymbol symbol, Compilation compilation, IrMethod? classified,
    IrBodyScanResult? scan)
{
    /// <summary>The method symbol.</summary>
    public IMethodSymbol Symbol { get; } = symbol;

    /// <summary>The compilation containing the method.</summary>
    public Compilation Compilation { get; } = compilation;

    /// <summary>The classified IR body, or <see langword="null"/> when the builder returned none.</summary>
    public IrMethod? Classified { get; } = (classified is null) == (scan is null)
        ? classified
        : throw new ArgumentException("Classified body and scan result must both be present or both be null.");

    /// <summary>The scan result of <see cref="Classified"/>, or <see langword="null"/> when it is null.</summary>
    public IrBodyScanResult? Scan { get; } = scan;
}
