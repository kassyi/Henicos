using System.ComponentModel;

namespace Kassyi.GSharp.Core;

public static partial class ManagedGpuContext
{
    // The names of the C++ preludes, which the loop that the CPU lowering makes of a Gpu.GridStrideLoop
    // whose body splits the state machine calls (PreLoweringExpander); generated code reaches them through
    // using static

    /// <summary>The same as <see cref="GridStrideStart"/>. For generated code only.</summary>
    /// <returns>The rank of the thread, or 2^31 when it is 2^31 or more.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static uint __gsharp_grid_stride_start() => GridStrideStart();

    /// <summary>The same as <see cref="GridStrideStep"/>. For generated code only.</summary>
    /// <returns>The number of threads, or 2^31 when it is 2^31 or more.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static uint __gsharp_grid_stride_step() => GridStrideStep();

    /// <summary>The same as <see cref="GridStrideCount"/>. For generated code only.</summary>
    /// <param name="count">The count argument of the loop.</param>
    /// <returns><paramref name="count"/>, or 0 when it is negative.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static uint __gsharp_grid_stride_count(int count) => GridStrideCount(count);

    /// <summary>The int loop variable of that loop; the index is below the count, so below 2^31.
    /// For generated code only.</summary>
    /// <param name="index">The unsigned index of the loop.</param>
    /// <returns><paramref name="index"/> as an <see cref="int"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static int __gsharp_grid_stride_index(uint index) => (int)index;
}
