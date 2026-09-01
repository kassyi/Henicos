// ReSharper disable All
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace TestSpace;

[InlineArray(0)]
public struct ZeroLengthInlineArray
{
    private int _element0;
}

public static class InvalidInlineArrayKernelContainer
{
    [GpuKernel]
    public static void BadInlineArrayKernel(ZeroLengthInlineArray[] array)
    {
    }
}
