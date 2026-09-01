// ReSharper disable All
using Kassyi.GSharp.Core;

public class TestClass
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public void MyKernel(int[] data)
    {
        foreach (var item in data)
        {
        }
    }
}
