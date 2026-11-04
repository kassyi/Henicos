#nullable enable
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The registry of <see cref="KnownDivergence"/> entries that <see cref="DifferentialRunner.Run"/> applies to every case
/// (design §5, D2). A matrix test that finds a new divergence files an issue (M1 or M2) and adds a line here; the PR that
/// fixes it removes the line, since the entry then fails.
/// </summary>
public static class KnownDivergences
{
    // The cases of the entries below, named through nameof so that a renamed kernel cannot leave an entry behind
    private const string SubWarpConditionalShuffle =
        $"{nameof(BarrierAndWarpDifferentialKernels)}.{nameof(BarrierAndWarpDifferentialKernels.SubWarpConditionalShuffleKernel)}";
    private const string GenericStructStaticSharedMemory =
        $"{nameof(BarrierAndWarpDifferentialKernels)}.{nameof(BarrierAndWarpDifferentialKernels.GenericStructStaticSharedMemoryKernel)}";
    private const string InterlockedReturnExpression =
        $"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.InterlockedReturnExpressionKernel)}";

    /// <summary>Gets every registered entry.</summary>
    public static IReadOnlyList<KnownDivergence> All { get; } =
    [
        // new("IntegerMatrixKernels.Int32ArithmeticMatrixKernel", "a>>>b", DiffPath.Native | DiffPath.Cuda, Issue: 722),

        // Warp.ShuffleXor after sub-warp conditional ShuffleDown on CPU emulator (#1001)
        new(SubWarpConditionalShuffle, "reconvergedFullWarpXor16", DiffPath.Managed | DiffPath.Native, Issue: 1001),

        // generic struct static method SharedMemory.Allocate<T> collides across type arguments (#543)
        new(GenericStructStaticSharedMemory, "SharedSlot<int>", DiffPath.Managed | DiffPath.Native, Issue: 543),

        // Interlocked long overloads return unsigned long long in C++, and signed 32-bit add overflow UB in CUDA prelude (#1057, #1058)
        new(InterlockedReturnExpression, "add64_lt_zero", DiffPath.Native | DiffPath.Cuda, Issue: 1057), // Interlocked.Add(ref long) returns unsigned long long in C++ (#1057)
        new(InterlockedReturnExpression, "exch64_sar_sign", DiffPath.Native | DiffPath.Cuda, Issue: 1057), // Interlocked.Exchange(ref long) returns unsigned long long in C++ (#1057)
        new(InterlockedReturnExpression, "cas64_lt_zero", DiffPath.Native | DiffPath.Cuda, Issue: 1057), // Interlocked.CompareExchange(ref long) returns unsigned long long in C++ (#1057)
        new(InterlockedReturnExpression, "add32_wrap_min", DiffPath.Cuda, Issue: 1058), // __gsharp_atomicAdd(int*, int) performs signed int addition without unsigned cast, triggering C++ signed overflow UB under nvcc (#1058)
    ];
}
