using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.WmmaUserMarker;

// A struct of the user that implements IWmmaShape is an ordinary struct, not a WMMA shape marker: read through default
public struct DefaultShapeTag : IWmmaShape
{
    public int X;

    public DefaultShapeTag(int x)
    {
        X = x;
    }
}

public static partial class UserMarkerDefaultKernels
{
    [GpuKernel]
    public static void UserMarkerDefaultKernel(int[] output)
    {
        output[0] = default(DefaultShapeTag).X + 1;
    }
}
