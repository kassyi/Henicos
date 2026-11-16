namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>How a value that C# computed in <c>int</c> is brought back to a smaller type.</summary>
internal abstract record Narrowing
{
    /// <summary>
    /// A cast in the context it is written in: it throws when the value is out of range, as the checked operators of
    /// generic math do.
    /// </summary>
    public sealed record Cast : Narrowing;

    /// <summary>The low bits, <c>((T)((value) &amp; mask))</c>: the cast never throws.</summary>
    public sealed record LowBits(int Mask) : Narrowing;

    /// <summary>
    /// The low bits sign-extended, <c>((T)(((value) &lt;&lt; shift) &gt;&gt; shift))</c>: the cast never throws.
    /// </summary>
    public sealed record SignExtended(int Shift) : Narrowing;
}
