namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>Why <see cref="IrSplitLowering.Apply"/> could not lower a method body.</summary>
internal enum IrSplitLoweringFailureKind
{
    /// <summary>The lowering succeeded.</summary>
    None,

    /// <summary>A statement's split plan fell back (<see cref="IrSpillFallback"/>).</summary>
    Fallback,

    /// <summary>The method body contains an <see cref="Statements.IrOpaqueStatement"/>.</summary>
    OpaqueStatement,

    /// <summary>Applying a statement's split plan was skipped (<see cref="IrSpillApplySkip"/>).</summary>
    Skip
}
