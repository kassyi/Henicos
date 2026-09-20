using Kassyi.GSharp.Core;
using System;
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.Shared.Analyzers.HostLoop;

public class TestLoopAnalyzerSafePath
{
    [GpuKernel]
    public static float KernelTest() => default;

    [GpuKernel]
    public static void VoidKernel() { }

    public void HostMethod()
    {
        // 1. Array returning launch outside loops
        var outside = Launch_KernelTest();

        // 2. Void returning launch inside loops
        bool c = false;
        while (c) { Launch_VoidKernel(); }
        do { Launch_VoidKernel(); } while (c);
        for (int i = 0; i < 10; i++) { Launch_VoidKernel(); }
        foreach (var x in new int[0]) { Launch_VoidKernel(); }

        // 3. Array returning launch inside local function / lambda (they are not executed inside the loop directly)
        for (int i = 0; i < 10; i++)
        {
            Action a = () => { var r5 = Launch_KernelTest(); };
            
            void LocalFunc() { var r6 = Launch_KernelTest(); }
        }
    }

    public static float[] Launch_KernelTest() => new float[0];
    public static void Launch_VoidKernel() { }
}
