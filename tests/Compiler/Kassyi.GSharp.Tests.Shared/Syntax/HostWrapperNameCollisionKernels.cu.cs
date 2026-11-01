using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax.HostWrapperCollision;

// Adversarial user types in the same namespace as the kernel class (#406)
public struct IntPtr
{
    public int Dummy;
}

public static class System
{
    public static int NotTheFramework;
}

public static class CudaDriverApi
{
    public static int NotTheDriver;
}

[SkipSnapshotConsistency("Covered by HostWrapperNameCollisionGeneratorTests")]
public static partial class HostWrapperNameCollisionKernels
{
    public struct CollisionPayload
    {
        public int[] Data;
        public int Scale;
    }

    [GpuKernel]
    public static int ExecuteCollisionKernel(
        int hModule,
        int hFunc,
        int[] args,
        int totalThreads,
        int dllPath,
        int hLib,
        int fnPtr,
        int invoke,
        int totalBlocks,
        int blockSize,
        int startBlock,
        int endBlock,
        int native,
        int host,
        ref int pinned,
        CollisionPayload payload)
    {
        int idx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (idx < args.Length)
        {
            args[idx] = hModule + hFunc + totalThreads + dllPath + hLib + fnPtr + invoke +
                        totalBlocks + blockSize + startBlock + endBlock + native + host +
                        pinned + payload.Data[idx] * payload.Scale;
        }

        if (idx == args.Length - 1)
        {
            pinned += 10;
        }

        return args[idx] + 1;
    }
}
