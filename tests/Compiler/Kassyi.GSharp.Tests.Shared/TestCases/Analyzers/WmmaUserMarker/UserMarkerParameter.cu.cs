using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.WmmaUserMarker;

// A struct of the user that implements both IWmmaShape and IWmmaLayout is an ordinary struct, not a WMMA marker: a
// parameter of a device function
public struct ParameterMarkerTag : IWmmaShape, IWmmaLayout
{
    public int X;

    public ParameterMarkerTag(int x)
    {
        X = x;
    }
}

public static partial class UserMarkerParameterKernels
{
    [GpuDeviceFunction]
    public static int ReadTag(ParameterMarkerTag tag) => tag.X;

    [GpuKernel]
    public static void UserMarkerParameterKernel(int[] output)
    {
        output[0] = ReadTag(new ParameterMarkerTag(4));
    }
}
