// ReSharper disable All
using Kassyi.GSharp.Core;

namespace TestSpace;

public struct CircularStructA
{
    public CircularStructB FieldB;
}

public struct CircularStructB
{
    public CircularStructA FieldA;
}

public static class CircularStructKernelContainer
{
    [GpuKernel]
    public static void CircularKernel(CircularStructA[] input)
    {
    }
}
