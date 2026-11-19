namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

public static partial class GpuBigIntDivRemKernels
{
    /// <summary>
    /// Warms up the module error sites and resolves the <c>CUfunction</c> handle for
    /// <see cref="ReciprocalNewtonRaphsonMegaKernel"/> in the active CUDA context.
    /// </summary>
    internal static IntPtr ResolveReciprocalNewtonRaphsonMegaKernel()
    {
        WarmupFunctionPointerReciprocalNewtonRaphsonMegaKernel();
        return __gsharp_hfunc_ReciprocalNewtonRaphsonMegaKernel.Resolve();
    }
}
