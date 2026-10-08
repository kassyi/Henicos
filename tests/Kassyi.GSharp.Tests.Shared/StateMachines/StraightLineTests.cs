using System.Threading.Tasks;
using Xunit;


namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public class StraightLineTests : StateMachineTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    public Task Lower_StraightLine_TwoBarriers() => VerifyStateMachineLower(() =>
    {
        int a = 1;
        Gpu.SyncThreads();
        int b = a + 2;
        Gpu.SyncThreads();
        int c = b * 3;
    });

    [Fact]
    [Category(TestCategory.Compiler)]
    public Task Lower_StraightLine_NoBarriers() => VerifyStateMachineLower(() =>
    {
        int a = 1;
        int b = a + 2;
    });
}
