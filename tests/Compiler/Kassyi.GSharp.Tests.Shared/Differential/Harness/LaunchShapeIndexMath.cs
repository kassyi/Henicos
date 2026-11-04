#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Authoritative 3D thread/block linearization and warp mask math matching CUDA specification and G# intrinsics.
/// </summary>
public static class LaunchShapeIndexMath
{
    /// <summary>
    /// Computes the 1D block-local thread ID: <c>tid.x + tid.y * bdim.x + tid.z * bdim.x * bdim.y</c>.
    /// </summary>
    public static int BlockLinearTid(in GpuThreadContext ctx) =>
        ctx.ThreadIdxX
        + (ctx.ThreadIdxY * ctx.BlockDimX)
        + (ctx.ThreadIdxZ * ctx.BlockDimX * ctx.BlockDimY);

    /// <summary>
    /// Computes the 1D grid-local block ID: <c>bid.x + bid.y * gdim.x + bid.z * gdim.x * gdim.y</c>.
    /// </summary>
    public static long GridLinearBid(in GpuThreadContext ctx) =>
        ctx.BlockIdxX
        + ((long)ctx.BlockIdxY * ctx.GridDimX)
        + ((long)ctx.BlockIdxZ * ctx.GridDimX * ctx.GridDimY);

    /// <summary>
    /// Computes the 64-bit global linear thread ID across a 3D grid of 3D blocks.
    /// </summary>
    public static long GlobalLinearGid(in GpuThreadContext ctx)
    {
        long threadsPerBlock = (long)ctx.BlockDimX * ctx.BlockDimY * ctx.BlockDimZ;
        return (GridLinearBid(in ctx) * threadsPerBlock) + BlockLinearTid(in ctx);
    }

    /// <summary>
    /// Computes the expected converged <c>Warp.ActiveMask()</c> for a thread in a block of <paramref name="threadsPerBlock"/> threads.
    /// </summary>
    public static uint ExpectedConvergedActiveMask(int blockLinearTid, int threadsPerBlock)
    {
        int warpStart = blockLinearTid & ~31;
        int remaining = threadsPerBlock - warpStart;
        if (remaining >= 32)
        {
            return 0xFFFF_FFFFu;
        }

        if (remaining <= 0)
        {
            return 0u;
        }

        return (1u << remaining) - 1u;
    }
}
