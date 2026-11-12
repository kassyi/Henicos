#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Runs the launches of an oracle under <see cref="KernelExecution.CSharp"/>: the generated launcher runs the kernel's
/// own C# method once per GPU thread, one thread after another, in the order given here. The body is the method the
/// user wrote, not the managed path's rewrite of it, so it is the C# definition of the result.
/// </summary>
/// <remarks>
/// A kernel that reaches a barrier, a warp intrinsic, shared memory, an operator macro or a StackLinq floating-point
/// sum cannot run one thread at a time: its launcher throws <see cref="InvalidOperationException"/> before any thread
/// runs. Such a case needs a reference model (<see cref="DifferentialCase.Reference"/>).
/// </remarks>
public static class SequentialOracle
{
    /// <summary>
    /// Runs <paramref name="launch"/> under <see cref="KernelExecution.CSharp"/> with the GPU threads of each launch in
    /// <paramref name="order"/>.
    /// </summary>
    /// <param name="shape">
    /// The launch shape; every launch of <paramref name="launch"/> must have its thread count.
    /// </param>
    /// <param name="order">The order of the threads.</param>
    /// <param name="launch">
    /// The launch, e.g. <c>() =&gt; Kernels.LaunchAddKernel(a, b, r, gridDimX: 2, blockDimX: 64)</c>.
    /// </param>
    /// <exception cref="DifferentialHarnessException">
    /// A launch has another thread count than <paramref name="shape"/>.
    /// </exception>
    public static void Run(LaunchShape shape, ThreadOrder order, Action launch)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(launch);
        using (GpuExecution.Use(KernelExecution.CSharp))
        using (CSharpKernelRunner.UseThreadOrder(count => Enumerate(shape, order, count)))
        {
            launch();
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> once per GPU thread of <paramref name="shape"/>, as a generated launcher does
    /// under <see cref="KernelExecution.CSharp"/>, with each thread's context set and in the order that
    /// <see cref="Run"/> chose (the order of the linear index outside it). For C# methods that have no launcher:
    /// kernels compiled at run time without the G# generator, and the oracle twins of the fuzzer, which are plain
    /// methods.
    /// </summary>
    /// <param name="shape">The launch shape.</param>
    /// <param name="body">The call of the method for one thread.</param>
    public static void RunEachThread(LaunchShape shape, Action body)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(body);
        CSharpKernelRunner.Run(
            shape.GridX,
            shape.GridY,
            shape.GridZ,
            shape.BlockX,
            shape.BlockY,
            shape.BlockZ,
            _ => body()
        );
    }

    /// <summary>
    /// Runs <paramref name="body"/> once per GPU thread of <paramref name="shape"/> in <paramref name="order"/>:
    /// <see cref="RunEachThread(LaunchShape, Action)"/> inside <see cref="Run"/>.
    /// </summary>
    /// <param name="shape">The launch shape.</param>
    /// <param name="order">The order of the threads.</param>
    /// <param name="body">The call of the method for one thread.</param>
    public static void RunEachThread(LaunchShape shape, ThreadOrder order, Action body) =>
        Run(shape, order, () => RunEachThread(shape, body));

    // The ids of the order, for a launch of the shape's thread count: a launch with other dimensions than the shape
    // would give the threads other coordinates than the paths under test see
    private static int[] Enumerate(LaunchShape shape, ThreadOrder order, int count) =>
        count == shape.ThreadCount
            ? order.Enumerate(shape)
            : throw new DifferentialHarnessException(
                $"the oracle launched {count} threads, but the launch shape {shape} has {shape.ThreadCount}");
}
