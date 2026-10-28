namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

using static FermatTestMath;

/// <summary>
/// The clean-up of <see cref="QuadraticDeviceModulus"/> when the capture of a CUDA Graph fails: the capture is ended (the
/// stream works again), the partial graph is destroyed, nothing recorded runs, and the original error reaches the caller.
/// </summary>
/// <remarks>The failure is injected with the internal hook <c>CaptureFault</c>; a counting wrapper observes the graph calls.</remarks>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class QuadraticDeviceModulusCaptureTests
{
    private const int E = 40;
    private const uint Base = 3;

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SquareState_CaptureFails_EndsTheCaptureAndDestroysThePartialGraph()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        var provider = new CountingGraphProvider(GSharpKernelProvider.Instance);
        using var modulus = new QuadraticDeviceModulus(new QuadraticFoldModulus(E), isCpu: false, provider);
        modulus.Write(SquaringRegister.State, ToWords(Base, modulus.Words));
        modulus.CaptureFault = static i =>
        {
            if (i == 3)
            {
                throw new InjectedCaptureException();
            }
        };

        // One eager warm-up squaring, then the capture of the other 9 fails at its fourth squaring.
        // mutant: QuadraticDeviceModulus.cs GetOrCaptureGraph catch `provider.DestroyGraph(provider.EndCapture(stream), IntPtr.Zero)` -> `provider.EndCapture(stream)`
        Assert.Throws<InjectedCaptureException>(() => modulus.SquareState(10));
        Assert.Equal(1, provider.BeginCaptures);
        Assert.Equal(1, provider.EndCaptures);
        Assert.Equal(1, provider.DestroyedGraphs);
        Assert.Equal(0, provider.Instantiations);

        // The recorded squarings never ran (the state is u_1), and the stream is no longer capturing: it reads, and a
        // new capture works.
        // mutant: QuadraticDeviceModulus.cs GetOrCaptureGraph catch: the clean-up call removed (the stream stays capturing)
        modulus.CaptureFault = null;
        Assert.Equal(Squarings(E, Base, 1), FromWords(modulus.ReadCanonical(SquaringRegister.State)));
        modulus.SquareState(5);
        Assert.Equal(Squarings(E, Base, 6), FromWords(modulus.ReadCanonical(SquaringRegister.State)));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SquareState_CaptureAndCleanupFail_ReportsBothErrors()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        var provider = new CountingGraphProvider(GSharpKernelProvider.Instance) { FailAfterEndCapture = true };
        using var modulus = new QuadraticDeviceModulus(new QuadraticFoldModulus(E), isCpu: false, provider);
        modulus.Write(SquaringRegister.State, ToWords(Base, modulus.Words));
        modulus.CaptureFault = static _ => throw new InjectedCaptureException();

        // The clean-up fails too (after the capture has ended): both errors are reported, the original first.
        var exception = Assert.Throws<AggregateException>(() => modulus.SquareState(3));
        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.IsType<InjectedCaptureException>(exception.InnerExceptions[0]);
        Assert.IsType<InvalidOperationException>(exception.InnerExceptions[1]);

        provider.FailAfterEndCapture = false;
        modulus.CaptureFault = null;
        modulus.SquareState(2);
        Assert.Equal(Squarings(E, Base, 3), FromWords(modulus.ReadCanonical(SquaringRegister.State)));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Dispose_GraphDestroyFails_ReleasesEverythingOnceAndReportsIt()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        var provider = new CountingGraphProvider(GSharpKernelProvider.Instance);
        var modulus = new QuadraticDeviceModulus(new QuadraticFoldModulus(E), isCpu: false, provider);
        modulus.Write(SquaringRegister.State, ToWords(Base, modulus.Words));
        modulus.SquareState(3); // the warm-up and one cached graph of length 2
        provider.FailDestroy = true;

        // The failure is reported after the buffers and the workspace are released; the graph left the cache first, so
        // a second Dispose neither destroys it again nor throws (review m1).
        // mutant: QuadraticDeviceModulus.cs Dispose `_graphs.Remove(length, out var entry)` -> `_graphs.TryGetValue(length, out var entry)`
        var exception = Assert.Throws<AggregateException>(modulus.Dispose);
        Assert.IsType<InvalidOperationException>(Assert.Single(exception.InnerExceptions));
        Assert.Equal(1, provider.DestroyAttempts);

        modulus.Dispose();
        Assert.Equal(1, provider.DestroyAttempts);
    }

    private sealed class InjectedCaptureException : Exception;

    /// <summary>Delegates every graph call and counts them; can fail right after ending a capture.</summary>
    private sealed class CountingGraphProvider(IGpuGraphProvider inner) : IGpuGraphProvider
    {
        public int BeginCaptures { get; private set; }

        public int EndCaptures { get; private set; }

        public int Instantiations { get; private set; }

        public int DestroyedGraphs { get; private set; }

        public bool FailAfterEndCapture { get; set; }

        public bool FailDestroy { get; set; }

        public int DestroyAttempts { get; private set; }

        public void BeginCapture(IntPtr stream)
        {
            BeginCaptures++;
            inner.BeginCapture(stream);
        }

        public IntPtr EndCapture(IntPtr stream)
        {
            EndCaptures++;
            var graph = inner.EndCapture(stream);
            if (FailAfterEndCapture)
            {
                inner.DestroyGraph(graph, IntPtr.Zero);
                throw new InvalidOperationException("Injected failure after ending the capture.");
            }

            return graph;
        }

        public IntPtr InstantiateGraph(IntPtr graph)
        {
            Instantiations++;
            return inner.InstantiateGraph(graph);
        }

        public void LaunchGraph(IntPtr graphExec, IntPtr stream) => inner.LaunchGraph(graphExec, stream);

        public void DestroyGraph(IntPtr graph, IntPtr graphExec)
        {
            DestroyAttempts++;
            if (FailDestroy)
            {
                // Release the graph for real, then report a failure, so that the test leaks nothing.
                inner.DestroyGraph(graph, graphExec);
                throw new InvalidOperationException("Injected failure destroying a graph.");
            }

            if (graph != IntPtr.Zero && graphExec == IntPtr.Zero)
            {
                DestroyedGraphs++;
            }

            inner.DestroyGraph(graph, graphExec);
        }
    }
}
