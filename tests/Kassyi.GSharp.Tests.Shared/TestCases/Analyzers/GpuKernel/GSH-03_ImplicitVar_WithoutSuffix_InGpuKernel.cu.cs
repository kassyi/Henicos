// ReSharper disable All
using Kassyi.GSharp.Core;

public class TestClass
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public void MyKernel()
    {
        var x = 10; // implicit int without 'u' or 'L' suffix
    }
}
