using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.WmmaUserMarker;

// A struct of the user that implements IWmmaShape is an ordinary struct, not a WMMA shape marker: declared as a local
public struct LocalShapeTag : IWmmaShape
{
    public int X;

    public LocalShapeTag(int x)
    {
        X = x;
    }
}

public static partial class UserMarkerLocalKernels
{
    [GpuKernel]
    public static void UserMarkerLocalKernel(int[] output)
    {
        LocalShapeTag tag = new LocalShapeTag(5);
        output[0] = tag.X;
    }
}
