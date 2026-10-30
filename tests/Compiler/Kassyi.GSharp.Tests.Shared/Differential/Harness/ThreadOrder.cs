#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>How a <see cref="ThreadOrder"/> arranges the threads.</summary>
public enum ThreadOrderKind
{
    /// <summary>Linear ids 0 to n - 1.</summary>
    Forward,

    /// <summary>Linear ids n - 1 down to 0.</summary>
    Reverse,

    /// <summary>A permutation drawn from a seed.</summary>
    Permuted,
}

/// <summary>
/// The order in which <see cref="SequentialOracle"/> runs the threads of a launch. The oracle runs each seed in three
/// orders; a kernel whose output depends on the order has a data race or reads a non-commutative atomic, which is
/// undefined on the GPU (semantics charter §3).
/// </summary>
/// <param name="Kind">The arrangement.</param>
/// <param name="Seed">The seed of a <see cref="ThreadOrderKind.Permuted"/> order; 0 otherwise.</param>
public readonly record struct ThreadOrder(ThreadOrderKind Kind, int Seed)
{
    /// <summary>Gets the order 0 to n - 1.</summary>
    public static ThreadOrder Forward => new(ThreadOrderKind.Forward, 0);

    /// <summary>Gets the order n - 1 down to 0.</summary>
    public static ThreadOrder Reverse => new(ThreadOrderKind.Reverse, 0);

    /// <summary>Returns a permutation drawn from <paramref name="seed"/> (Fisher-Yates over <see cref="DiffRandom"/>).</summary>
    /// <param name="seed">The seed.</param>
    public static ThreadOrder Permutation(int seed) => new(ThreadOrderKind.Permuted, seed);

    /// <summary>Returns the linear thread ids of <paramref name="shape"/> in this order.</summary>
    /// <param name="shape">The launch.</param>
    public int[] Enumerate(LaunchShape shape)
    {
        int n = shape.ThreadCount;
        var ids = new int[n];
        for (int i = 0; i < n; i++)
        {
            ids[i] = Kind == ThreadOrderKind.Reverse ? n - 1 - i : i;
        }

        if (Kind == ThreadOrderKind.Permuted)
        {
            var rng = new DiffRandom(Seed);
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.NextBelow(i + 1);
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }
        }

        return ids;
    }

    /// <inheritdoc/>
    public override string ToString() => Kind == ThreadOrderKind.Permuted ? $"permuted (seed {Seed})" : Kind.ToString().ToLowerInvariant();
}
