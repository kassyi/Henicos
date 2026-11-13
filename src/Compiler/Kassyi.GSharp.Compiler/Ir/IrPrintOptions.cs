namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>What <see cref="IrPrinter"/> writes besides the nodes.</summary>
[Flags]
internal enum IrPrintOptions
{
    /// <summary>The nodes only.</summary>
    None = 0,

    /// <summary>The span after each expression, <c>[start..end]</c>, or <c>[~start..end]</c> when
    /// synthesized.</summary>
    Sources = 1,

    /// <summary>No <c>[split=..., writes=...]</c> after a classified call.</summary>
    WithoutClassifications = 2
}
