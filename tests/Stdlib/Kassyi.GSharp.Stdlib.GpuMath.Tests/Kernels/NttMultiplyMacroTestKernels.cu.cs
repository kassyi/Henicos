using System.Diagnostics.CodeAnalysis;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.Kernels;

[SkipSnapshotConsistency("Tested in NttMultiplyMacroTests")]
[ExcludeFromCodeCoverage]
public static partial class NttMultiplyMacroTestKernels
{
    [GpuDeviceFunction]
    private static void ForceEmitDualNttForMacroTest(DualNtt args)
    {
    }

    [GpuDeviceFunction]
    private static void ForceEmitNttResiduesForMacroTest(NttResidues residues)
    {
    }

    [GpuKernel(Cooperative = true)]
    public static void MultiplyByNttMacroKernel(
        NewtonRaphsonContext core,
        uint[] opA,
        uint[] opB,
        uint[] result,
        int numWordsA,
        int numWordsB,
        int numWordsResult)
    {
        GpuBigIntDivRemKernels.ExecuteNttMultiplyMacro(
            core, opA, opB, result, numWordsA, numWordsB, numWordsResult, BlockDimension.X);
    }
}
