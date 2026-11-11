// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Constants passed to a <c>ref readonly</c> parameter without a keyword. C# passes a temporary for a constant and
/// warns (CS9193); the managed copy must not write <c>in</c> for it, which does not compile (CS8156).
/// </summary>
public static partial class RefReadOnlyConstantKernels
{
    private const int FieldConstant = 4;

    [GpuDeviceFunction]
    public static int ReadOnlyId(ref readonly int x) => x;

    // The pragmas are inside the bodies so that the managed copies, which copy the bodies only, keep them
    [GpuKernel]
    public static void ConstantLocalToRefReadOnlyKernel(int[] r)
    {
        const int localConstant = 3;
        int tid = ThreadIndex.X;
#pragma warning disable CS9193 // A constant is passed to the ref readonly parameter on purpose
        r[tid] = ReadOnlyId(localConstant);
#pragma warning restore CS9193
    }

    [GpuKernel]
    public static void ConstantFieldToRefReadOnlyKernel(int[] r)
    {
        int tid = ThreadIndex.X;
#pragma warning disable CS9193 // A constant is passed to the ref readonly parameter on purpose
        r[tid] = ReadOnlyId(FieldConstant);
#pragma warning restore CS9193
    }
}
