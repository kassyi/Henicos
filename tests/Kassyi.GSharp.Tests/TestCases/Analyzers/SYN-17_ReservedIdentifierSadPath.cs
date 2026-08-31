using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN_17_ReservedIdentifierSadPath
{
    [GpuKernel]
    public static void ReservedIdentifierKernel(int[] data)
    {
        // Sad Path: Double underscore prefix is reserved
        int __invalid_var = 10;
        data[ThreadIndex.X] = __invalid_var;

        // Sad Path: Double underscore anywhere in the name is reserved in C++
        int hoge__boo = 15;
        data[ThreadIndex.X] = hoge__boo;

        // Happy Path: Single underscore is allowed (discard or normal variable)
        int _valid_var = 20;
        _ = _valid_var;
        data[ThreadIndex.X] = _valid_var;
    }

    // Sad Path: Double underscore in parameter name
    [GpuDeviceFunction]
    private static void InvalidParameterMethod(int __param)
    {
        // do nothing
    }

    // Sad Path: Double underscore in method name
    [GpuDeviceFunction]
    private static void __InvalidMethod()
    {
        // do nothing
    }
}
