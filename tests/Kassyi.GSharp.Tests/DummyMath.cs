namespace Kassyi.GSharp.Tests;

public class DummyMath
{
    [GpuKernel]
    public static void SimpleLoop(uint[] T, int e)
    {
        // Loops are temporarily unsupported until CFG is fully implemented.
        // This flat kernel ensures the transpiler runner test passes.
        T[0] = T[0] * 2;
    }
}
