#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// One kernel under differential test: its launch, its inputs per seed, and the definition of the right result, either
/// the kernel's own C# run by <see cref="SequentialOracle"/> (<see cref="Oracle"/>) or a reference model written in the
/// test (<see cref="Reference"/>).
/// </summary>
/// <param name="name">The name the report and <see cref="KnownDivergence.Case"/> use: <c>Class.KernelMethod</c>.</param>
public sealed class DifferentialCase(string name)
{
    /// <summary>Gets the name of the case.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the launch; <see cref="LaunchShape.Rows"/> is the row count passed to <see cref="Inputs"/>.</summary>
    public required LaunchShape Shape { get; init; }

    /// <summary>Gets the function that builds the buffers for a seed: <c>(rng, rows) =&gt; new DiffBuffers().In(...).Out(...)</c>.</summary>
    public required Func<DiffRandom, int, DiffBuffers> Inputs { get; init; }

    /// <summary>Gets the launch on the path under test: <c>(b, s) =&gt; Kernels.LaunchXxxKernel(..., gridDimX: s.GridX, blockDimX: s.BlockX)</c>.</summary>
    public required Action<DiffBuffers, LaunchShape> Launch { get; init; }

    /// <summary>
    /// Gets the call of the kernel's C# body, which <see cref="SequentialOracle"/> runs once per thread in three orders:
    /// <c>b =&gt; Kernels.XxxKernel(b.Get&lt;int&gt;("a"), ...)</c>. Only for kernels without barriers, warp intrinsics, shared
    /// memory, operator macros or StackLinq floating-point sums (design §2.1).
    /// </summary>
    public Action<DiffBuffers>? Oracle { get; init; }

    /// <summary>
    /// Gets the reference model, run once without a thread context: the GPU definition written in C# for a kernel the
    /// oracle cannot run. It must not call the code under test (the managed rewrite, the host StackLinq).
    /// </summary>
    public Action<DiffBuffers>? Reference { get; init; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
