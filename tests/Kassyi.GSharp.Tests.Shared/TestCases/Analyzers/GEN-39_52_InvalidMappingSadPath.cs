using Kassyi.GSharp.Core;

[assembly: BclIntrinsicMap("NonExistentType.DoesNotExist", "NonExistentMethod", new[] { "System.Int32" }, "__invalid_intrinsic")]

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

[System.AttributeUsage(System.AttributeTargets.Method)]
public class UnknownCustomAttribute : System.Attribute
{
}

public static class GEN_39_52_InvalidMappingSadPath
{
    [GpuKernel]
    [UnknownCustom]
    public static void KernelWithUnknownAttribute(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = data[tid] + 1;
    }

    [GpuKernel]
    public static void KernelWithUnsupportedSyntax(int[] data)
    {
        // Object creation in GPU kernel (emits GSH0001 diagnostic error)
        var obj = new object();
        data[0] = obj.GetHashCode();
    }
}
