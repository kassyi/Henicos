#nullable enable
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Library device functions and iterators defined in a separate file to verify
/// cross-file iterator resolution through local variables.
/// </summary>
public static partial class CrossFileIteratorLibrary
{
    /// <summary>
    /// Helper device function called within an iterator argument expression.
    /// </summary>
    [GpuDeviceFunction]
    public static int H(int n) => n * 3;

    /// <summary>
    /// Core device iterator yielding an element for a given base.
    /// </summary>
    [GpuDeviceFunction]
    public static IEnumerable<int> Inner(int n)
    {
        yield return n + 1;
    }

    /// <summary>
    /// Shape (a): Iterator initializing a local variable from <see cref="Inner"/> with an argument
    /// expression containing a function call to <see cref="H"/>, then enumerating it.
    /// </summary>
    [GpuDeviceFunction]
    public static IEnumerable<int> OuterWithCallArg(int n)
    {
        var it = Inner(n * 2 + H(n));
        foreach (var x in it)
        {
            yield return x;
        }
    }

    /// <summary>
    /// Shape (b): Iterator initializing a local variable from <see cref="Inner"/> and enumerating
    /// it across two separate <c>foreach</c> loops.
    /// </summary>
    [GpuDeviceFunction]
    public static IEnumerable<int> OuterTwice(int n)
    {
        var it = Inner(n);
        foreach (var x in it)
        {
            yield return x;
        }

        foreach (var x in it)
        {
            yield return x;
        }
    }
}
