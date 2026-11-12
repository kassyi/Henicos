using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

// A kernel in a class that is not partial, or in a partial class inside a type that is not (GSH0036), gets nothing from
// the generator, so CS0260 in the generated C# does not hide the diagnostics; the direct call is the witness (GSH0031)
public static class NonPartialKernels
{
    [GpuKernel]
    public static void InNonPartialClass(int[] o) { o[0] = 42; }
}

public struct NonPartialOuter
{
    public static partial class Inner
    {
        [GpuKernel]
        public static void InNonPartialOuter(int[] o) { o[0] = 42; }
    }
}

public static partial class NonPartialWitness
{
    [GpuKernel]
    public static void Valid(int[] o) { o[0] = 42; }
}

public static class NonPartialHost
{
    public static void Run(int[] o) => NonPartialWitness.Valid(o);
}
