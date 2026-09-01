// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public struct NestedChildStruct
{
    public int Value;
}

public struct NestedParentStruct
{
    public int Id;
    public NestedChildStruct Child;
}

public static class SYN_17_TargetTypedNewKernels
{
    [GpuDeviceFunction]
    public static NestedChildStruct CreateChild(NestedChildStruct child)
    {
        return child;
    }

    // Happy Path: Target-typed new() variable declaration & method argument
    [GpuKernel]
    public static void HappyPathKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        NestedChildStruct c1 = new();
        c1.Value = tid + 100;

        NestedChildStruct c2 = CreateChild(new());
        c2.Value = tid + 200;

        output[tid] = c1.Value + c2.Value;
    }

    // Edge Cases: Nested struct initializer with Target-typed new()
    [GpuKernel]
    public static void NestedInitializerKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        NestedParentStruct p = new NestedParentStruct { Id = tid, Child = new() };
        p.Child.Value = tid * 5;
        output[tid] = p.Id + p.Child.Value;
    }
}
