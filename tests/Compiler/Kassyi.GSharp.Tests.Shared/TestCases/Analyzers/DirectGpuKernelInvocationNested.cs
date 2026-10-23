using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class NestedInvocationKernels
{
    [GpuKernel]
    public static void TargetKernel()
    {
    }

    // HAPPY PATH: a lambda or local function nested in a GPU function is device code.
    [GpuKernel]
    public static void KernelCallingFromLocalFunction()
    {
        void LocalFunc()
        {
            TargetKernel();
        }

        LocalFunc();
    }

    [GpuDeviceFunction]
    public static void DeviceFunctionCallingFromLambda()
    {
        Action call = () => TargetKernel();
        call();
    }
}

public static class NestedInvocationHost
{
    public static void HostLambda()
    {
        // SAD PATH: a lambda in host code is still host code (GSH0031).
        Action call = () => NestedInvocationKernels.TargetKernel();
        call();
    }
}
