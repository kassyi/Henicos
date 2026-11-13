namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// What a temporary of <see cref="IrSpillRewriter"/> holds, which decides the prefix of its C++ name.
/// </summary>
internal enum IrTemporaryKind
{
    /// <summary>An operand evaluated before a split, or the read of a compound assignment target.</summary>
    Argument,

    /// <summary>The value of a warp operation that splits.</summary>
    Warp,

    /// <summary>The value of a call that suspends.</summary>
    Call,

    /// <summary>The value of a <c>?:</c>, <c>&amp;&amp;</c> or <c>||</c> written as an <see langword="if"/>.</summary>
    Condition
}
