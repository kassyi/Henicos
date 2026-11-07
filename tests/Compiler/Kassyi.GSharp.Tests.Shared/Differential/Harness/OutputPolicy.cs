#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>The kinds of <see cref="OutputPolicy"/>.</summary>
public enum OutputPolicyKind
{
    /// <summary>Every bit of the cell matches.</summary>
    Exact,

    /// <summary>Every bit matches, except that any NaN matches any NaN (floating-point cells).</summary>
    Bitwise,

    /// <summary>The cell is not compared.</summary>
    Ignore,

    /// <summary>The cells of each column are compared as an order-independent multiset.</summary>
    Multiset,

    /// <summary>Floating-point cells are compared within a monotonic Sign-Magnitude linear ULP distance.</summary>
    Ulp,
}

/// <summary>
/// How the cells of a buffer are compared with the oracle.
/// </summary>
public sealed class OutputPolicy
{
    private OutputPolicy(
        OutputPolicyKind kind,
        object? customComparer = null,
        uint maxUlp = 0,
        string? reason = null,
        bool allowInfinityBoundary = false
    )
    {
        Kind = kind;
        CustomComparer = customComparer;
        MaxUlp = maxUlp;
        Reason = reason;
        AllowInfinityBoundary = allowInfinityBoundary;
    }

    /// <summary>
    /// Gets the policy for integer results, conversion results and the final values of commutative atomics: every bit
    /// matches.
    /// </summary>
    public static OutputPolicy Exact { get; } = new(OutputPolicyKind.Exact);

    /// <summary>
    /// Gets the policy for correctly rounded floating-point results (<c>+ - * /</c>, <c>Math.Sqrt</c>, comparisons,
    /// conversions, sequential sums): every bit matches, but NaN matches NaN whatever its sign and payload, since .NET
    /// passes an input NaN through and PTX returns the canonical NaN. Same as <see cref="Exact"/> for other types.
    /// </summary>
    public static OutputPolicy Bitwise { get; } = new(OutputPolicyKind.Bitwise);

    /// <summary>
    /// Gets the policy for cells whose value the GPU leaves to the thread order, such as the old values an atomic returns;
    /// they are left out of the comparison and of the oracle's order check, and need a property check of their own.
    /// </summary>
    public static OutputPolicy Ignore { get; } = new(OutputPolicyKind.Ignore);

    /// <summary>
    /// Gets the policy for order-independent multiset outputs (such as atomic old values with uniform step): each column is
    /// sorted independently using a total-order bit sort before cell-by-cell comparison.
    /// </summary>
    public static OutputPolicy Multiset { get; } = new(OutputPolicyKind.Multiset);

    /// <summary>
    /// Creates a floating-point ULP tolerance policy using Sign-Magnitude monotonic linear grid distance.
    /// <paramref name="reason"/> must document the official error bound (e.g. CUDA Programming Guide table bound).
    /// When <paramref name="allowInfinityBoundary"/> is <see langword="true"/>, <c>MaxValue</c> and <c>+Infinity</c>
    /// (or <c>MinValue</c> and <c>-Infinity</c>) are treated as adjacent 1 ULP neighbors.
    /// </summary>
    public static OutputPolicy Ulp(uint maxUlp, string reason, bool allowInfinityBoundary = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        return new OutputPolicy(OutputPolicyKind.Ulp, customComparer: null, maxUlp, reason, allowInfinityBoundary);
    }

    /// <summary>Creates a floating-point ULP tolerance policy with a default reason label.</summary>
    public static OutputPolicy Ulp(uint maxUlp) =>
        new(OutputPolicyKind.Ulp, customComparer: null, maxUlp, "ULP tolerance", allowInfinityBoundary: false);

    /// <summary>Creates a field-wise struct comparison policy using a <see cref="StructFieldCheckContext"/> callback.</summary>
    public static OutputPolicy ForStruct<T>(Action<StructFieldCheckContext, T, T> compareFields, OutputPolicy? basePolicy = null)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(compareFields);
        return new OutputPolicy(
            basePolicy?.Kind ?? OutputPolicyKind.Exact,
            new StructElementComparer<T>(compareFields),
            basePolicy?.MaxUlp ?? 0,
            basePolicy?.Reason,
            basePolicy?.AllowInfinityBoundary ?? false
        );
    }

    /// <summary>Creates a field-wise struct comparison policy using a policy-aware predicate.</summary>
    public static OutputPolicy ForStruct<T>(
        Func<T, T, OutputPolicy, bool> fieldPredicate,
        Func<T, string>? formatter = null,
        OutputPolicy? basePolicy = null
    )
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(fieldPredicate);
        return new OutputPolicy(
            basePolicy?.Kind ?? OutputPolicyKind.Exact,
            new StructElementComparer<T>(fieldPredicate, formatter),
            basePolicy?.MaxUlp ?? 0,
            basePolicy?.Reason,
            basePolicy?.AllowInfinityBoundary ?? false
        );
    }

    /// <summary>Creates a field-wise struct comparison policy using a boolean predicate.</summary>
    public static OutputPolicy ForStruct<T>(
        Func<T, T, bool> fieldPredicate,
        Func<T, string>? formatter = null,
        OutputPolicy? basePolicy = null
    )
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(fieldPredicate);
        return new OutputPolicy(
            basePolicy?.Kind ?? OutputPolicyKind.Exact,
            new StructElementComparer<T>(fieldPredicate, formatter),
            basePolicy?.MaxUlp ?? 0,
            basePolicy?.Reason,
            basePolicy?.AllowInfinityBoundary ?? false
        );
    }

    /// <summary>Creates a field-wise struct comparison policy using an <see cref="IElementComparer{T}"/>.</summary>
    public static OutputPolicy ForStruct<T>(IElementComparer<T> comparer, OutputPolicy? basePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        return new OutputPolicy(
            basePolicy?.Kind ?? OutputPolicyKind.Exact,
            comparer,
            basePolicy?.MaxUlp ?? 0,
            basePolicy?.Reason,
            basePolicy?.AllowInfinityBoundary ?? false
        );
    }

    /// <summary>Gets the kind of the policy.</summary>
    public OutputPolicyKind Kind { get; }

    /// <summary>Gets the maximum allowed ULP distance when <see cref="Kind"/> is <see cref="OutputPolicyKind.Ulp"/>.</summary>
    public uint MaxUlp { get; }

    /// <summary>Gets the documented reason for the ULP tolerance when <see cref="Kind"/> is <see cref="OutputPolicyKind.Ulp"/>.</summary>
    public string? Reason { get; }

    /// <summary>Gets a value indicating whether <c>MaxValue</c> and <c>Infinity</c> are treated as 1-ULP adjacent under <see cref="OutputPolicyKind.Ulp"/>.</summary>
    public bool AllowInfinityBoundary { get; }

    internal object? CustomComparer { get; }

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        OutputPolicyKind.Ulp => AllowInfinityBoundary
            ? $"Ulp({MaxUlp}, \"{Reason}\", allowInf)"
            : $"Ulp({MaxUlp}, \"{Reason}\")",
        _ => Kind.ToString(),
    };
}
