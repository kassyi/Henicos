using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A kernel whose parameter is a private struct with an array field: its launchers and marshaling methods are private,
/// so only the class launches it (<c>PrivateStructParameterLaunchers.cs</c>).
/// </summary>
public static partial class PrivateStructParameterKernels
{
    private struct Bag
    {
        public int[] Values;
        public int Scale;
    }

    [GpuKernel]
    private static void ScaleKernel(Bag bag)
    {
        int i = ThreadIndex.X;
        bag.Values[i] = bag.Values[i] * bag.Scale + i;
    }
}
