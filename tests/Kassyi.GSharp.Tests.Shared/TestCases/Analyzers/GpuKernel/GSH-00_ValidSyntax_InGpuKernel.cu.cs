// ReSharper disable All
using Kassyi.GSharp.Core;

public class TestClass
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public void MyKernel(int[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            var x = 10u; // explicitly uint
            int y = 10; // explicitly typed
        }
    }
}
