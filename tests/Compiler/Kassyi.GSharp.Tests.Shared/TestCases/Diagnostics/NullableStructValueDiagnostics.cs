#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Diagnostics;

public struct NullableNameHolder
{
    public string Name;
}

public static partial class NullableStructValueKernels
{
    [GpuKernel]
    public static void NullableReferenceFieldKernel(NullableNameHolder? holder, int[] output)
    {
        output[0] = holder.HasValue ? 1 : 0;
    }
}
