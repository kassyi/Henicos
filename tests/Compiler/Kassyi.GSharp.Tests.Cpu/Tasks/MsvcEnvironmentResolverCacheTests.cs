#nullable enable
using System.Diagnostics;
using Kassyi.GSharp.Compiler.Tasks.Helpers;
using Kassyi.GSharp.Compiler.Tasks.Toolchains;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

/// <summary>
/// The process-wide cache of the <c>vcvarsall.bat</c> environment keeps a result, not the exception of a resolution
/// that was cancelled or failed: the next build of the process resolves again.
/// </summary>
[Category(TestCategory.Compiler)]
public class MsvcEnvironmentResolverCacheTests
{
    private const string Vswhere = @"X:\__dummy__\vswhere.exe";

    private const string OtherVswhere = @"X:\__dummy__\other\vswhere.exe";

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetVcvarsEnvironment_FirstResolutionCancelled_ResolvesAgain()
    {
        // The entry of the cancelled resolution is not kept: the second call is not the first one's cancellation
        var runner = new CountingRunner();
        var toolchain = new MsvcToolchain(runner, disableCache: false, Vswhere);
        var architecture = NewArchitecture();

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(
            () => toolchain.GetVcvarsEnvironment(architecture, cancelled.Token));
        Assert.Equal(1, runner.Calls);

        var second = toolchain.GetVcvarsEnvironment(architecture, CancellationToken.None);

        Assert.Null(second);
        Assert.Equal(2, runner.Calls);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetVcvarsEnvironment_FirstResolutionThrows_ResolvesAgain()
    {
        var runner = new CountingRunner { ThrowOnFirstCall = true };
        var toolchain = new MsvcToolchain(runner, disableCache: false, Vswhere);
        var architecture = NewArchitecture();

        Assert.Throws<IOException>(() => toolchain.GetVcvarsEnvironment(architecture, CancellationToken.None));

        var second = toolchain.GetVcvarsEnvironment(architecture, CancellationToken.None);

        Assert.Null(second);
        Assert.Equal(2, runner.Calls);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetVcvarsEnvironment_ResolvedOnce_IsNotRunAgain()
    {
        // A result (here: no Visual Studio) is kept: the second call does not run vswhere again
        var runner = new CountingRunner();
        var toolchain = new MsvcToolchain(runner, disableCache: false, Vswhere);
        var architecture = NewArchitecture();

        Assert.Null(toolchain.GetVcvarsEnvironment(architecture, CancellationToken.None));
        Assert.Null(toolchain.GetVcvarsEnvironment(architecture, CancellationToken.None));

        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetVcvarsEnvironment_OtherVswhereOverride_ResolvesItsOwn()
    {
        // The key holds the vswhere override: the second toolchain does not get the result of the first
        var architecture = NewArchitecture();
        var firstRunner = new CountingRunner();
        var secondRunner = new CountingRunner();

        new MsvcToolchain(firstRunner, disableCache: false, Vswhere)
            .GetVcvarsEnvironment(architecture, CancellationToken.None);
        new MsvcToolchain(secondRunner, disableCache: false, OtherVswhere)
            .GetVcvarsEnvironment(architecture, CancellationToken.None);

        Assert.Equal(1, firstRunner.Calls);
        Assert.Equal(1, secondRunner.Calls);
    }

    // The cache is static: each test uses an architecture of its own
    private static string NewArchitecture() => "probe_" + Guid.NewGuid().ToString("N");

    // vswhere fails with exit code 1 (no Visual Studio, so the resolution gives null) unless the token is cancelled
    // or the first call is to throw
    private sealed class CountingRunner : IProcessRunner
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public bool ThrowOnFirstCall { get; init; }

        public Task<ProcessResult> RunAsync(
            ProcessStartInfo startInfo,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Run(startInfo, cancellationToken));

        public ProcessResult Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnFirstCall && call == 1)
            {
                throw new IOException("vswhere could not be started");
            }

            return new ProcessResult(1, string.Empty, "no Visual Studio");
        }
    }
}
