using Kassyi.GSharp.Core;
using System.Linq;

public class TestClass
{
    [GpuKernel]
    public void MyKernel(int[] data)
    {
        var query = from x in data select x;
    }
}
