namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// One node that the copy writes differently from the declaration. Each kind carries what its form needs and nothing
/// else; <see cref="GenericCopyEmitter"/> writes the form from it and decides nothing.
/// </summary>
/// <param name="NodeIndex">The position of the node in <see cref="CopyNodeIndex.List"/> of the declaration.</param>
internal abstract record CopyRewrite(int NodeIndex)
{
    /// <summary>
    /// A name bound to a type parameter: the type argument, fully qualified (<c>global::System.Int32</c>).
    /// </summary>
    public sealed record TypeArgument(int NodeIndex, ITypeSymbol Type) : CopyRewrite(NodeIndex);

    /// <summary>
    /// The name of a generic method whose type arguments C# inferred: the same name with the type arguments written
    /// (<c>Helper&lt;global::System.Int32&gt;</c>), so that no non-generic overload can take the call in the copy.
    /// </summary>
    public sealed record ExplicitTypeArguments(int NodeIndex, ImmutableArray<ITypeSymbol> TypeArguments)
        : CopyRewrite(NodeIndex);

    /// <summary>
    /// A call of an extension method on a receiver whose type depends on a type parameter: the static form
    /// (<c>global::Ns.Exts.Ext&lt;global::System.Int32&gt;(receiver)</c>), so that no instance member of the type
    /// argument can take the call in the copy.
    /// </summary>
    public sealed record StaticExtensionCall(
        int NodeIndex,
        IMethodSymbol Method,
        ImmutableArray<ITypeSymbol> TypeArguments) : CopyRewrite(NodeIndex);

    /// <summary>
    /// <c>T.Zero</c>, <c>T.One</c>, <c>T.AdditiveIdentity</c> or <c>T.MultiplicativeIdentity</c> of a type whose BCL
    /// implementation is private: the constant they return (<c>((global::System.Int32)0)</c>).
    /// </summary>
    /// <param name="Type">The type of the constant.</param>
    /// <param name="Value">0 or 1.</param>
    /// <param name="Opaque">
    /// Whether the constant is written so that C# cannot fold it: an operator on two constants is folded at compile
    /// time, where a division by zero or an overflow is a compile error in the copy and a run-time exception in the
    /// declaration.
    /// </param>
    public sealed record IdentityConstant(int NodeIndex, ITypeSymbol Type, int Value, bool Opaque)
        : CopyRewrite(NodeIndex);

    /// <summary>
    /// A static member that the type declares publicly and the interface member of the declaration stands for:
    /// <c>T.MaxValue</c> as <c>global::System.Int32.MaxValue</c>.
    /// </summary>
    /// <param name="Opaque">As in <see cref="IdentityConstant"/>.</param>
    public sealed record StaticMember(int NodeIndex, ITypeSymbol Type, string Name, bool Opaque)
        : CopyRewrite(NodeIndex);

    /// <summary>
    /// <c>nameof</c> of something that names a type parameter: the string it gives in the declaration.
    /// </summary>
    public sealed record NameOf(int NodeIndex, string Text) : CopyRewrite(NodeIndex);

    /// <summary>
    /// The call <c>x.M(a)</c> of an interface method through a type parameter, whose implementation is explicit or a
    /// default one: <c>Helper&lt;S&gt;(ref x, a)</c>, with the helper of <see cref="GenericCopyPlan.Helpers"/>.
    /// </summary>
    /// <param name="HelperIndex">The position of the helper in <see cref="GenericCopyPlan.Helpers"/>.</param>
    public sealed record ConstrainedCall(int NodeIndex, int HelperIndex) : CopyRewrite(NodeIndex);

    /// <summary>A call that returns its argument: <c>T.Abs(x)</c> of an unsigned type as <c>(x)</c>.</summary>
    public sealed record ArgumentCall(int NodeIndex) : CopyRewrite(NodeIndex);

    /// <summary>A call that returns <see langword="false"/>: <c>T.IsNaN(x)</c> of an integer type.</summary>
    public sealed record FalseCall(int NodeIndex) : CopyRewrite(NodeIndex);

    /// <summary>
    /// A call of <c>System.Math</c> that gives what the generic math member gives: <c>Math.Max(a, b)</c> for the
    /// <c>T.MaxMagnitude(a, b)</c> of an unsigned type.
    /// </summary>
    /// <param name="Name">The name of the <c>Math</c> method.</param>
    /// <param name="ResultType">The type the result is cast to, when <c>Math</c> returns a wider one
    /// (<c>char</c>).</param>
    public sealed record MathCall(int NodeIndex, string Name, ITypeSymbol? ResultType) : CopyRewrite(NodeIndex);

    /// <summary>
    /// An arithmetic or bitwise operator, or <c>~</c> / unary <c>+</c>, on a type that C# promotes to <c>int</c>: the
    /// C#
    /// operator brought back to the type, which is what the generic math operator computes.
    /// </summary>
    public sealed record NarrowedOperator(int NodeIndex, ITypeSymbol Type, Narrowing Narrowing)
        : CopyRewrite(NodeIndex);

    /// <summary>
    /// A shift of a type that C# promotes to <c>int</c>: the shift count masked to the width of the type, the operand
    /// of
    /// <c>&gt;&gt;&gt;</c> of a signed type read as its low bits, and the result brought back.
    /// </summary>
    /// <param name="CountMask">The mask of the shift count: the width of the type minus one.</param>
    /// <param name="LeftMask">The mask of the operand that <c>&gt;&gt;&gt;</c> reads as its low bits, if it
    /// does.</param>
    public sealed record NarrowedShift(
        int NodeIndex,
        ITypeSymbol Type,
        int CountMask,
        int? LeftMask,
        Narrowing Narrowing) : CopyRewrite(NodeIndex);

    /// <summary>
    /// Unary minus on <c>uint</c>, <c>ulong</c> or <c>nuint</c>, whose C# negation is another type or does not exist:
    /// zero minus the operand in the type itself (<c>(((global::System.UInt32)0) - a)</c>).
    /// </summary>
    public sealed record TypedNegation(int NodeIndex, ITypeSymbol Type) : CopyRewrite(NodeIndex);

    /// <summary>Unary minus on a type that C# promotes to <c>int</c>: <c>0 - a</c> brought back to the type.</summary>
    public sealed record NarrowedNegation(int NodeIndex, ITypeSymbol Type, Narrowing Narrowing)
        : CopyRewrite(NodeIndex);

    /// <summary>
    /// <c>a /= b</c> of a type that C# promotes to <c>int</c>: in a checked context it throws for <c>(-128, -1)</c> of
    /// <c>sbyte</c>, where the generic one returns <c>-128</c>. <c>a = &lt;a / b, narrowed&gt;</c>; the target is read
    /// twice, so it has no side effects.
    /// </summary>
    public sealed record NarrowedCompoundDivision(int NodeIndex, ITypeSymbol Type, Narrowing Narrowing)
        : CopyRewrite(NodeIndex);

    /// <summary>
    /// A compound shift of a type that C# promotes to <c>int</c>, which masks its count with 31 where the generic one
    /// masks it with the width of the type: <c>a = &lt;a op b, masked and narrowed&gt;</c>; the target is read twice,
    /// so
    /// it has no side effects.
    /// </summary>
    /// <param name="Operator">The binary shift operator of the compound assignment.</param>
    /// <param name="CountMask">The mask of the shift count.</param>
    /// <param name="LeftMask">For <c>&gt;&gt;&gt;=</c> of a signed type, the mask of the operand.</param>
    public sealed record NarrowedCompoundShift(
        int NodeIndex,
        ITypeSymbol Type,
        SyntaxKind Operator,
        int CountMask,
        int? LeftMask,
        Narrowing Narrowing) : CopyRewrite(NodeIndex);
}
