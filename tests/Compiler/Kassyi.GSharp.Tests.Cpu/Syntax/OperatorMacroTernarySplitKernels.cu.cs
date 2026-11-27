using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Syntax;

namespace Kassyi.GSharp.Tests.Cpu.Syntax;

/// <summary>
/// A conditional expression (<c>?:</c>) with an operator macro on one branch and a <c>Warp.ActiveMask()</c> split
/// point on the other: CPU lowering turns <c>?:</c> into an <c>if</c> whose condition temporary must be classified as
/// C++ storage (<see cref="Compiler.Lowering.LoweringTempNames.IsConditionTemp"/>) so the operator macro target takes
/// its address.
/// </summary>
[SkipSnapshotConsistency("CPU-only lowering test for ?: with operator macro and Warp.ActiveMask() split")]
public static partial class OperatorMacroTernarySplitKernels
{
    [GpuKernel]
    public static void TernaryMacroWithActiveMaskKernel(int[] input, int[] output, int[] count)
    {
        int t = ThreadIndex.X;
        TruthValue a = new TruthValue(input[t]);
        TruthValue two = new TruthValue(2);
        TruthValue c = default;
        c = input[t] > 0 ? a & two : OperatorMacroConditionalSplitKernels.MaskValue(count, t);
        output[t] = c.Value;
    }
}
