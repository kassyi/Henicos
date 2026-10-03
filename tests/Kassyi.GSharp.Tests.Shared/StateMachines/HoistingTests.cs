#pragma warning disable CS0219
using System.Threading.Tasks;
using Xunit;


namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public class HoistingTests : StateMachineTestBase
{
    [Fact]
    [Trait("Category", "Compiler")]
    public Task Hoist_VariablesAcrossBarrier() => VerifyHoist(() =>
    {
        int a = 1;
        int b = 2; // Conservatively hoisted because its syntactic block overlaps the barrier.
        Gpu.SyncThreads();
        int c = a + 3; // 'a' is hoisted (accessed via ctx->a), 'c' is kept local.
    });
}
