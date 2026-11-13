namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>How the closed copy of a generic declaration writes one node of the declaration.</summary>
internal enum CopyRewriteKind
{
    /// <summary>
    /// A name bound to a type parameter: the type argument, fully qualified (<c>global::System.Int32</c>).
    /// </summary>
    TypeArgument,

    /// <summary>
    /// The name of a generic method whose type arguments C# inferred: the same name with the type arguments written
    /// (<c>Helper&lt;global::System.Int32&gt;</c>), so that no non-generic overload can take the call in the copy.
    /// </summary>
    ExplicitTypeArguments,

    /// <summary>
    /// A call of an extension method on a receiver whose type depends on a type parameter: the static form
    /// (<c>global::Ns.Exts.Ext&lt;global::System.Int32&gt;(receiver)</c>), so that no instance member of the type
    /// argument can take the call in the copy.
    /// </summary>
    StaticExtensionCall,

    /// <summary>
    /// <c>T.Zero</c>, <c>T.One</c>, <c>T.AdditiveIdentity</c> or <c>T.MultiplicativeIdentity</c> of a built-in numeric
    /// type, which implements them explicitly: the constant they return (<c>((global::System.Int32)0)</c>).
    /// </summary>
    IdentityConstant,

    /// <summary>
    /// <c>nameof</c> of something that names a type parameter: the string it gives in the declaration.
    /// </summary>
    NameOfConstant,

    /// <summary>
    /// An arithmetic or bitwise operator, or <c>~</c> / unary <c>+</c>, on a type that C# promotes to <c>int</c>
    /// (<see cref="BuiltinNumerics.IsPromoted"/>): the C# operator brought back to the type, which is what the generic
    /// math operator computes. A checked operator converts back with a cast in the context it is written in
    /// (<c>((global::System.Byte)(a + b))</c>, throwing where <c>op_CheckedAddition</c> throws); an unchecked one keeps
    /// the low bits first, so that the cast never throws even inside <c>checked</c>
    /// (<c>((global::System.Byte)((a / b) &amp; 0xFF))</c>, <c>((global::System.SByte)(((a / b) &lt;&lt; 24) &gt;&gt;
    /// 24))</c>).
    /// </summary>
    NarrowedOperator,

    /// <summary>
    /// A shift of a type that C# promotes to <c>int</c>: the shift count masked to the width of the type, the operand
    /// of <c>&gt;&gt;&gt;</c> of a signed type read as its low bits, and the result brought back as an unchecked
    /// <see cref="NarrowedOperator"/> (<c>((global::System.Byte)((a &lt;&lt; (n &amp; 7)) &amp; 0xFF))</c>).
    /// </summary>
    NarrowedShift,

    /// <summary>
    /// Unary minus on a type whose C# negation returns another type (<see cref="BuiltinNumerics.NegationLeavesType"/>):
    /// zero minus the operand, in the type itself for <c>uint</c>, <c>ulong</c> and <c>nuint</c>
    /// (<c>(((global::System.UInt32)0) - a)</c>) and brought back like <see cref="NarrowedOperator"/> for the promoted
    /// types.
    /// </summary>
    NarrowedNegation,
}
