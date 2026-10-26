using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// On CUDA, Vector2 is 8-byte aligned on the device and 4-byte aligned on the host, so the launcher converts the struct
// member by member, through a host mirror that names the hidden field of the captured parameter (#495)
public struct CapturedConverted(int a)
{
    public System.Numerics.Vector2 V;

    public int Read => a;
}

// The same struct inside another one that the launcher converts
public struct CapturedConvertedOuter
{
    public int Before;
    public CapturedConverted Inner;
}

public static partial class PrimaryConstructorCapturedConvertedKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CapturedConvertedKernel(ref CapturedConverted data, int[] output)
    {
        output[0] = data.Read;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NestedCapturedConvertedKernel(CapturedConvertedOuter[] data, int[] output)
    {
        output[0] = data[0].Inner.Read;
    }
}
