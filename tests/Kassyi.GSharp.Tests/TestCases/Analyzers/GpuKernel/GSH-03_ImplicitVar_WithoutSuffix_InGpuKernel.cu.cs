using Kassyi.GSharp.Core;

public class TestClass
{
    [GpuKernel]
    public void MyKernel()
    {
        var x = 10; // implicit int without 'u' or 'L' suffix
    }
}
