// ReSharper disable All
namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GSH_00_ValidSyntax_InGpuKernel;

using Kassyi.GSharp.Core;

public class TestClass
{
    [GpuKernel]
    public void MyKernel(int[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            var x = 10u; // explicitly uint
            int y = 10; // explicitly typed
        }
    }
}
