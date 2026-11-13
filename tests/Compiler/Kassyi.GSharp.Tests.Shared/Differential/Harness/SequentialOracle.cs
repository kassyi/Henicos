#nullable enable
using System;
using Kassyi.GSharp.Core;
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
/// sum cannot run one thread at a time: its launcher throws <see cref="CSharpExecutionUnsupportedException"/> before
/// any thread runs, which <see cref="Run"/> turns into a <see cref="DifferentialHarnessException"/>, also when the case
/// expects exceptions. Such a case needs a reference model (<see cref="DifferentialCase.Reference"/>).
/// </remarks>
public static class SequentialOracle
{
    /// <summary>
    /// Runs <paramref name="launch"/> under <see cref="KernelExecution.CSharp"/> with the GPU threads of each launch in
    /// <paramref name="order"/>.
    /// </summary>
    /// <param name="shape">
    /// The launch shape; every launch of <paramref name="launch"/> must have its dimensions.
    /// </param>
    /// <param name="order">The order of the threads.</param>
    /// <param name="launch">
    /// The launch, e.g. <c>() =&gt; Kernels.LaunchAddKernel(a, b, r, gridDimX: 2, blockDimX: 64)</c>.
    /// </param>
    /// <exception cref="DifferentialHarnessException">
    /// A launch has other dimensions than <paramref name="shape"/>, or a kernel cannot run as C#
    /// (<see cref="CSharpExecutionUnsupportedException"/>, the inner exception).
    /// </exception>
    public static void Run(LaunchShape shape, ThreadOrder order, Action launch)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(launch);
        try
        {
            using (GpuExecution.Use(KernelExecution.CSharp))
            using (CSharpKernelRunner.UseThreadOrder(dimensions => Enumerate(shape, order, dimensions)))
            {
                launch();
            }
        }
        catch (CSharpExecutionUnsupportedException ex)
        {
            // The oracle has no C# value, which is a fault of the case, not an exception of the kernel to compare
            throw new DifferentialHarnessException(
                $"the oracle cannot run as C#; give the case a Reference: {ex.Message}",
                ex
            );
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

    // The ids of the order, for a launch of the shape's dimensions: a launch with other dimensions than the shape,
    // even with as many threads, would give the threads other coordinates than the paths under test see
    private static int[] Enumerate(LaunchShape shape, ThreadOrder order, CSharpLaunchDimensions launch) =>
        launch.GridDimX == shape.GridX && launch.GridDimY == shape.GridY && launch.GridDimZ == shape.GridZ &&
        launch.BlockDimX == shape.BlockX && launch.BlockDimY == shape.BlockY && launch.BlockDimZ == shape.BlockZ
            ? order.Enumerate(shape)
            : throw new DifferentialHarnessException(
                $"the oracle launched grid=({launch.GridDimX},{launch.GridDimY},{launch.GridDimZ}) " +
                $"block=({launch.BlockDimX},{launch.BlockDimY},{launch.BlockDimZ}), but the launch shape is {shape}");
}
