#nullable enable
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Constants;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// The device copy of ln 2 kept by <see cref="GpuWorkspace.Constants"/>.
/// </summary>
/// <remarks>
/// The expected values are <see cref="GpuBigFloatConstants.GetLn2Fixed"/>, which <see cref="GpuBigFloatConstantsTests"/>
/// checks against the independent interval oracle.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
[Collection(TestCollections.GlobalStateSequential)]
public class GpuBigFloatConstantCacheTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigFloatConstantCacheTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    // A view holds floor(ln 2 · 2^F) for F = 32 · ceil(bits / 32), before and after the constant grows; a smaller
    // request is the top words of the uploaded value.
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    // mutant: GpuBigFloatConstantCache.cs GetLn2 `var offset = _ln2FractionBits / 32 - words;` -> `var offset = 0;`
    // (the view starts at the low words)
    // mutant: GpuBigFloatConstantCache.cs GetLn2 `32 * words > _ln2FractionBits` -> `32 * words > _ln2FractionBits + 32`
    // (a request one word beyond the upload reads past the buffer's top words)
    public void GetLn2_ViewsAreFloorsAtWordPrecision()
    {
        var constants = _workspace.Constants;

        AssertLn2(constants.GetLn2(64), 64);
        AssertLn2(constants.GetLn2(65), 96);
        AssertLn2(constants.GetLn2(1000), 1024);
        AssertLn2(constants.GetLn2(33), 64);
        AssertLn2(constants.GetLn2(1), 32);
    }

    // A disposed cache rejects requests.
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void GetLn2_AfterDispose_Throws()
    {
        var cache = new GpuBigFloatConstantCache(_workspace);
        cache.GetLn2(64);
        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cache.GetLn2(64));
    }

    private static void AssertLn2(GpuBigUInt view, int fractionBits)
    {
        Assert.Equal(fractionBits / 32, view.NumWords);
        Assert.Equal(GpuBigFloatConstants.GetLn2Fixed(fractionBits), view.ToBigInteger());
    }
}
