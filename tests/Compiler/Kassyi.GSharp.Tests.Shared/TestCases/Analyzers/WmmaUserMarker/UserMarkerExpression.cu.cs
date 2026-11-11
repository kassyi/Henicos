using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.WmmaUserMarker;

// A struct of the user that implements IWmmaLayout is an ordinary struct, not a WMMA layout marker: created in an
// expression without a local
public struct ExpressionLayoutTag : IWmmaLayout
{
    public int X;

    public ExpressionLayoutTag(int x)
    {
        X = x;
    }
}

public static partial class UserMarkerExpressionKernels
{
    [GpuKernel]
    public static void UserMarkerExpressionKernel(int[] output)
    {
        output[0] = new ExpressionLayoutTag(5).X;
    }
}
