// ReSharper disable All
namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GSH_03_ImplicitVar_WithoutSuffix_InGpuKernel;

using Kassyi.GSharp.Core;

public class TestClass
{
    [GpuKernel]
    public void MyKernel()
    {
        var x = 10; // implicit int without 'u' or 'L' suffix
    }
}
