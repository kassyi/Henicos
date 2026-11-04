#nullable enable
using System;
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// One kernel under differential test: its launch shapes, its inputs per seed, and the definition of the right result, either
/// the kernel's own C# run by <see cref="SequentialOracle"/> (<see cref="Oracle"/>) or a reference model written in the
/// test (<see cref="Reference"/>).
/// </summary>
/// <param name="name">The name the report and <see cref="KnownDivergence.Case"/> use: <c>Class.KernelMethod</c>.</param>
public sealed record DifferentialCase(string name)
{
    private readonly LaunchShape? _singleShape;
    private readonly ExceptionPolicy? _staticExceptionPolicy;

    /// <summary>Gets the name of the case.</summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets or initializes a single launch shape. Returns <c>Shapes[0]</c> when <see cref="Shapes"/> is set.
    /// </summary>
    public LaunchShape Shape
    {
        get => Shapes is { Count: > 0 } ? Shapes[0] : _singleShape ?? throw new InvalidOperationException("Shape is not set.");
        init => _singleShape = value;
    }

    /// <summary>Gets or initializes the launch shapes evaluated across all seeds.</summary>
    public IReadOnlyList<LaunchShape>? Shapes { get; init; }

    /// <summary>Gets or initializes additional launch shapes evaluated only on <c>seed == 0</c>.</summary>
    public IReadOnlyList<LaunchShape>? ExtraShapesSeed0Only { get; init; }

    /// <summary>Gets the effective launch shapes (including <see cref="ExtraShapesSeed0Only"/>).</summary>
    public IReadOnlyList<LaunchShape> EffectiveShapes => GetShapesForSeed(0);

    /// <summary>Returns the launch shapes to execute for the specified <paramref name="seed"/>.</summary>
    public IReadOnlyList<LaunchShape> GetShapesForSeed(int seed)
    {
        IReadOnlyList<LaunchShape> baseShapes = Shapes is { Count: > 0 }
            ? Shapes
            : _singleShape is not null
                ? [_singleShape]
                : [];

        if (baseShapes.Count == 0)
        {
            return [];
        }

        if (seed == 0 && ExtraShapesSeed0Only is { Count: > 0 })
        {
            return [..baseShapes, ..ExtraShapesSeed0Only];
        }

        return baseShapes;
    }

    /// <summary>Gets the function that builds the buffers for a seed and launch shape.</summary>
    public required Func<DiffRandom, LaunchShape, DiffBuffers> Inputs { get; init; }

    /// <summary>Gets the launch on the path under test.</summary>
    public required Action<DiffBuffers, LaunchShape> Launch { get; init; }

    /// <summary>
    /// Gets or initializes the pointer-based launch (<c>Launch{Kernel}Ptr</c>), executed on the CUDA path alongside <see cref="Launch"/>.
    /// </summary>
    public Action<DiffBuffers, LaunchShape>? LaunchPtr { get; init; }

    /// <summary>
    /// Gets the call of the kernel's C# body, which <see cref="SequentialOracle"/> runs once per thread in three orders.
    /// </summary>
    public Action<DiffBuffers, LaunchShape>? Oracle { get; init; }

    /// <summary>
    /// Gets the reference model, run once without a thread context: the GPU definition written in C# for a kernel the
    /// oracle cannot run.
    /// </summary>
    public Action<DiffBuffers, LaunchShape>? Reference { get; init; }

    /// <summary>Gets optional custom thread orders for <see cref="SequentialOracle"/>.</summary>
    public IReadOnlyList<ThreadOrder>? OracleOrders { get; init; }

    /// <summary>Gets optional explicit seeds for this case.</summary>
    public IReadOnlyList<int>? Seeds { get; init; }

    /// <summary>Gets optional default output policy metadata for this case.</summary>
    public OutputPolicy? Policy { get; init; }

    /// <summary>
    /// Gets or initializes a per-seed, per-shape expected exception policy hook. When this returns a non-null
    /// <see cref="Differential.ExceptionPolicy"/>, the oracle is skipped for that seed and each path in
    /// <see cref="ExceptionPolicy.Paths"/> must throw a matching exception.
    /// </summary>
    public Func<DiffRandom, LaunchShape, ExceptionPolicy?>? ExpectedException { get; init; }

    /// <summary>
    /// Gets or initializes a constant expected exception policy applied to all seeds when <see cref="ExpectedException"/> is not set.
    /// </summary>
    public ExceptionPolicy? ExceptionPolicy
    {
        get => _staticExceptionPolicy;
        init => _staticExceptionPolicy = value;
    }

    /// <summary>
    /// Gets or initializes how exceptions thrown by <see cref="Oracle"/> are handled when <see cref="ExpectedException"/> returns <see langword="null"/>.
    /// </summary>
    public OracleExceptionMode OracleExceptions { get; init; } = OracleExceptionMode.None;

    /// <summary>
    /// Gets or initializes the execution paths required to throw matching exceptions when <see cref="OracleExceptions"/>
    /// is <see cref="OracleExceptionMode.MatchOracle"/>.
    /// </summary>
    public DiffPath ExceptionPaths { get; init; } = DiffPaths.CheckedPaths();

    /// <summary>
    /// Gets or initializes the subprocess sampling granularity when executing CUDA <c>trap;</c> fault seeds.
    /// </summary>
    public CudaTrapSampling CudaTrapSampling { get; init; } = CudaTrapSampling.FirstFaultPerCase;

    /// <summary>Gets or initializes a post-launch property/invariant check over each path's buffers.</summary>
    public Func<DiffBuffers, LaunchShape, PropertyResult>? Verify { get; init; }

    /// <summary>
    /// Gets or initializes a post-launch property/invariant check comparing the pre-launch initial buffers (<c>initial</c>)
    /// and the post-launch buffers (<c>actual</c>) of each path.
    /// </summary>
    public Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? VerifyAgainstInitial { get; init; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
