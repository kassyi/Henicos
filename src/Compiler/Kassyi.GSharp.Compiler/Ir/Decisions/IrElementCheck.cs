namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Whether an array element read needs a runtime null check.</summary>
internal enum IrElementCheck
{
    /// <summary>No check: the receiver is proven non-null, or the index is <c>Index</c> / <c>Range</c>.</summary>
    None,

    /// <summary>The receiver may be null and must be checked at run time.</summary>
    NullChecked
}
