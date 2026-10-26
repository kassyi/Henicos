using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

public class KernelMemoryHandlerTests
{
    // Paths that return before any CUDA call: an empty copy and a canceled stream wait honour the token.
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void KernelMemoryHandler_ZeroWordsAndCanceledToken()
    {
        var handler = new KernelMemoryHandler();
        var host = new uint[4];
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        // A zero-word copy is complete at once, or canceled when the token already is.
        // mutant: KernelMemoryHandler.cs:107 `IsCancellationRequested ? ... : ...` condition -> `true` / `false`
        Assert.True(handler.CopyDeviceToHostAsync(host, IntPtr.Zero, 0, IntPtr.Zero, canceled.Token).IsCanceled);
        Assert.True(handler.CopyDeviceToHostAsync(host, IntPtr.Zero, 0, IntPtr.Zero).IsCompletedSuccessfully);

        // A canceled token cancels the wait, also on the null stream that has nothing to wait for.
        // mutant: KernelMemoryHandler.cs:181 block removed
        Assert.True(handler.SynchronizeStreamAsync(IntPtr.Zero, canceled.Token).IsCanceled);
    }
}
