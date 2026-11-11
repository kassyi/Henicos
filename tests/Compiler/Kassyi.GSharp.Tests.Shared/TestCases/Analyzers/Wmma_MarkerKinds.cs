using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// Types of the user that implement IWmmaShape or IWmmaLayout are ordinary structs; only the marker types of Core are
// WMMA markers and cannot be declared as locals
public struct UserWmmaShape : IWmmaShape
{
    public int X;
}

public struct UserWmmaLayout : IWmmaLayout
{
    public int X;
}

public static partial class WmmaMarkerKindsKernels
{
    [GpuKernel]
    public static void MarkerLocalsKernel(int[] output)
    {
        UserWmmaShape userShape = default;
        UserWmmaLayout userLayout = default;
        userShape.X = 1;
        userLayout.X = 2;
        output[0] = userShape.X + userLayout.X;

        WmmaM16N16K16 coreShape = default;
        WmmaRowMajor coreLayout = default;
    }
}
