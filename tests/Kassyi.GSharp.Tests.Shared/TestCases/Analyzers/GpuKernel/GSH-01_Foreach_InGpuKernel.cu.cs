// ReSharper disable All
using Kassyi.GSharp.Core;

public class TestClass
{
    [GpuKernel]
    public void MyKernel(int[] data)
    {
        foreach (var item in data)
        {
        }
    }
}
