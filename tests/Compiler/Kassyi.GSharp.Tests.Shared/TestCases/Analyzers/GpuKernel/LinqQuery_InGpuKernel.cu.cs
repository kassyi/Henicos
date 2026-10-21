// ReSharper disable All
using Kassyi.GSharp.Core;
using System.Linq;

public class TestClass
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public void MyKernel(int[] data)
    {
        var query = from x in data select x;
    }
}
