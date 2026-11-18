namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

public static partial class GpuBigIntSqrtKernels
{
    /// <summary>
    /// Warms up the module error sites and resolves the <c>CUfunction</c> handle for
    /// <see cref="InverseSqrtNewtonRaphsonMegaKernel"/> in the active CUDA context.
    /// </summary>
    internal static IntPtr ResolveInverseSqrtNewtonRaphsonMegaKernel()
    {
        WarmupFunctionPointerInverseSqrtNewtonRaphsonMegaKernel();
        return __gsharp_hfunc_InverseSqrtNewtonRaphsonMegaKernel.Resolve();
    }
}
