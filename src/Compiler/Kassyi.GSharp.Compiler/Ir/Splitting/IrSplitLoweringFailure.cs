namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>Why <see cref="IrSplitLowering.Apply"/> failed on a method body, and which statement caused it.</summary>
/// <param name="Kind">The category of failure.</param>
/// <param name="Fallback">
/// The fallback reason when <paramref name="Kind"/> is <see cref="IrSplitLoweringFailureKind.Fallback"/>.
/// </param>
/// <param name="Skip">
/// The skip reason when <paramref name="Kind"/> is <see cref="IrSplitLoweringFailureKind.Skip"/>.
/// </param>
/// <param name="Statement">The source span of the statement that caused the failure.</param>
internal readonly record struct IrSplitLoweringFailure(IrSplitLoweringFailureKind Kind, IrSpillFallback Fallback,
    IrSpillApplySkip Skip, IrSource Statement);
