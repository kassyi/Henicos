using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public struct TestStructGEN17
{
    public int Value;
}

public static class GEN17_RefOutReassignmentSadPathCase
{
    [GpuKernel]
    public static void KernelWithRefParamReassignment(ref TestStructGEN17 structRef, out int successFlag)
    {
        // Invalid reassignment of ref struct parameter itself in C# / GPU context
        structRef = new TestStructGEN17 { Value = 42 };
        successFlag = 1;
    }
}
