namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// What <see cref="InstanceSymbolMap.ResolveConstrained"/> found for a member of an interface that the declaration
/// reaches through a type parameter, and how the closed copy binds it.
/// </summary>
/// <param name="ClosedType">The type argument the member is reached through.</param>
internal abstract record ConstrainedMember(ITypeSymbol ClosedType)
{
    /// <summary>
    /// The public member of the type argument that implements it (<c>int.Max</c>, <c>Vec.op_Addition</c>).
    /// </summary>
    public sealed record Member(ITypeSymbol ClosedType, ISymbol Implementation) : ConstrainedMember(ClosedType);

    /// <summary>
    /// The C# operator of a built-in numeric type, which the type implements explicitly with that operator (the
    /// <c>op_Addition</c> of <c>IAdditionOperators&lt;int, int, int&gt;</c> is <c>+</c>).
    /// </summary>
    /// <param name="OperatorName">The metadata name of the operator.</param>
    public sealed record BuiltinOperator(ITypeSymbol ClosedType, string OperatorName) : ConstrainedMember(ClosedType);

    /// <summary>
    /// <c>Zero</c>, <c>One</c> and the identities of a built-in numeric type or <c>decimal</c>: a constant.
    /// </summary>
    /// <param name="Value">0 or 1.</param>
    public sealed record IdentityConstant(ITypeSymbol ClosedType, int Value) : ConstrainedMember(ClosedType);

    /// <summary>A static member the type declares publicly under the name of the interface member.</summary>
    public sealed record StaticMember(ITypeSymbol ClosedType, string Name) : ConstrainedMember(ClosedType);

    /// <summary>A call that returns its argument (<c>Abs</c> of an unsigned type).</summary>
    public sealed record ArgumentCall(ITypeSymbol ClosedType) : ConstrainedMember(ClosedType);

    /// <summary>
    /// A call that returns <see langword="false"/> whatever it is given (<c>IsNaN</c> of an integer type).
    /// </summary>
    public sealed record FalseCall(ITypeSymbol ClosedType) : ConstrainedMember(ClosedType);

    /// <summary>A call of <c>System.Math</c> that gives what the member gives.</summary>
    /// <param name="Name">The name of the <c>Math</c> method.</param>
    /// <param name="ResultType">The type the result is cast to, when <c>Math</c> returns a wider one.</param>
    public sealed record MathCall(ITypeSymbol ClosedType, string Name, ITypeSymbol? ResultType)
        : ConstrainedMember(ClosedType);

    /// <summary>
    /// An instance method that the generic method runs through its constraint as an explicit implementation or a
    /// default one: the closed copy cannot name it, so it calls it through a helper that is generic over the receiver
    /// (<see cref="ConstrainedHelper"/>).
    /// </summary>
    /// <param name="Method">The interface method, as the instance reads it.</param>
    public sealed record ConstrainedCall(ITypeSymbol ClosedType, IMethodSymbol Method) : ConstrainedMember(ClosedType);

    /// <summary>No closed C# spelling binds it.</summary>
    /// <param name="Reason">Why, for the message.</param>
    public sealed record Unsupported(ITypeSymbol ClosedType, string Reason) : ConstrainedMember(ClosedType);
}
