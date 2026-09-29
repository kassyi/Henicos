using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public partial class MathKernels
{
    [GpuKernel]
    public static void ProcessHeavySimulation(float[] data, float[] result, int length)
    {
        // Kernel code
    }
    
    public static void LaunchProcessHeavySimulation(float[] data, float[] result, int length, GridDimension grid, BlockDimension block)
    {
        // Generated launcher stub
    }
    
    public static void NormalMethod()
    {
    }
}

public class Invoker
{
    public void CallDirectly()
    {
        // SAD PATH: This should trigger GSH0031
        MathKernels.ProcessHeavySimulation(null, null, 0);
    }
    
    public void CallLauncher()
    {
        // HAPPY PATH: Should NOT trigger GSH0031
        MathKernels.LaunchProcessHeavySimulation(null, null, 0, default, default);
        MathKernels.NormalMethod();
    }
}
