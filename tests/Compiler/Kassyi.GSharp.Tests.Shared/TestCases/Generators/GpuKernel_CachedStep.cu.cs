using Kassyi.GSharp.Core;

namespace TestNamespace.Caching;

public struct CachingParticle
{
    public float X;
    public float Y;
}

// Covers the inputs whose caching matters: an overloaded kernel, a struct parameter and a device function
public static partial class CachingKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void ScaleKernel(float[] data, float factor)
    {
        int i = ThreadIndex.X;
        data[i] = Add(data[i] * factor, 0f);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void ScaleKernel(int[] data, int factor)
    {
        int i = ThreadIndex.X;
        data[i] = data[i] * factor;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void MoveKernel(CachingParticle[] particles)
    {
        int i = ThreadIndex.X;
        particles[i].X = particles[i].X + particles[i].Y;
    }

    [GpuDeviceFunction]
    public static float Add(float a, float b)
    {
        return a + b;
    }
}
