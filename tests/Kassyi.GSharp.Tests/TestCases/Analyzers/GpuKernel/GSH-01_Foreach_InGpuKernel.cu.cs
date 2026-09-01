// ReSharper disable All
namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GSH_01_Foreach_InGpuKernel;

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
