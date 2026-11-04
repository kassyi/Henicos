using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

[SkipSnapshotConsistency("Analyzer test case: StackArrayDiagnosticsTests")]
public static partial class CyclicStructParameter
{
    public struct Cyc
    {
        public Cyc Inner;
        public int X;
    }

    [GpuDeviceFunction]
    public static int ReadCyc(Cyc value)
    {
        return value.X;
    }

    [GpuKernel]
    public static void CycKernel(Cyc value, int[] output)
    {
        output[0] = ReadCyc(value);
    }
}
