using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.WmmaUserMarker;

// A struct of the user that implements IWmmaLayout is an ordinary struct, not a WMMA layout marker: a field of another
// struct
public struct FieldLayoutTag : IWmmaLayout
{
    public int X;

    public FieldLayoutTag(int x)
    {
        X = x;
    }
}

public struct FieldLayoutHolder
{
    public FieldLayoutTag Tag;
}

public static partial class UserMarkerFieldKernels
{
    [GpuKernel]
    public static void UserMarkerFieldKernel(int[] output)
    {
        FieldLayoutHolder holder = default;
        holder.Tag.X = 3;
        output[0] = holder.Tag.X;
    }
}
