#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Base of the GpuBigFloat expression tests: one CUDA context scope and one <see cref="GpuWorkspace"/> per test
/// instance, and the helpers that create numbers in that workspace.
/// </summary>
public abstract class GpuBigFloatWorkspaceTestBase : IDisposable
{
    private readonly GpuTestWorkspace _testWorkspace;

    protected GpuBigFloatWorkspaceTestBase()
    {
        CudaCapability.EnsureGpuAvailable();
        _testWorkspace = new GpuTestWorkspace(1024 * 1024, GpuExecutionEngine.GSharpAot);
    }

    protected GpuWorkspace Context => _testWorkspace.Workspace;

    public void Dispose() => _testWorkspace.Dispose();

    protected GpuBigFloat CreateFloat(long value, int exponent = 0, int numWords = 4) =>
        GpuBigFloatTestHelpers.CreateFloat(Context, value, exponent, numWords);

    protected T CreateNumber<T>(uint value)
        where T : struct, IGpuBigNumber<T> =>
        GpuBigFloatTestHelpers.CreateNumber<T>(Context, value);
}
