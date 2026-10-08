
namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class StackAllocFixedArrayTests:  TranspilerTestBase{
    [Fact()]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_GSH04_StackAlloc_HappyPath()
    {
    }

    [Fact()]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_GSH04_StackAlloc_InTightLoop()
    {
    }

    [Fact()]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_GSH04_StackAlloc_AsMethodArgument()
    {
    }
}
