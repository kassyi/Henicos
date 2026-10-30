#nullable enable
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
}

/// <summary>
/// How the cells of a buffer are compared with the oracle (design §2.4). Only <see cref="Exact"/>, <see cref="Bitwise"/>
/// and <see cref="Ignore"/> exist until #725 defines the ulp bounds (design D4).
/// </summary>
public sealed class OutputPolicy
{
    private OutputPolicy(OutputPolicyKind kind) => Kind = kind;

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

    /// <summary>Gets the kind of the policy.</summary>
    public OutputPolicyKind Kind { get; }

    /// <inheritdoc/>
    public override string ToString() => Kind.ToString();
}
