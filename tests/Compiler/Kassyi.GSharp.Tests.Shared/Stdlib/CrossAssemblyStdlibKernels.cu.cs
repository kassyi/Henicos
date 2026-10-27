using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

// Kernels that call the device functions of Kassyi.GSharp.Stdlib.GpuMath, another assembly built with the CUDA dialect
// (#433, #538): the native module imports what the standard library recorded, the managed fallback calls its C#
// methods and its managed state machines
public static partial class CrossAssemblyStdlibKernels
{
    // The probe of #433: a device function of the standard library
    [GpuKernel]
    public static void MontgomeryMuKernel(uint[] moduli, uint[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = CooperativeMath.ComputeMontgomeryMu(moduli[tid]);
    }

    // A function that suspends (warp shuffles), with a struct of the standard library as its parameter and result
    [GpuKernel]
    public static void WarpScanKernel(uint[] generate, uint[] propagate, uint[] result)
    {
        int lane = ThreadIndex.X;
        var scanned = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(generate[lane], propagate[lane]), lane);
        result[lane] = GpuBigIntKernels.PackCarryState(scanned);
    }
}
