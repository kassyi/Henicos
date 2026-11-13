namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// How the closed copy binds an interface member that the declaration reaches through a type parameter.
/// </summary>
internal enum ConstrainedMemberKind
{
    /// <summary>
    /// The public member of the type argument that implements it (<c>int.Max</c>, <c>Vec.op_Addition</c>).
    /// </summary>
    Member,

    /// <summary>
    /// The C# operator of a built-in numeric type, which the type implements explicitly with that operator
    /// (<c>int</c>'s <c>IAdditionOperators&lt;int, int, int&gt;.op_Addition</c> is <c>+</c>).
    /// </summary>
    BuiltinOperator,

    /// <summary><c>Zero</c>, <c>One</c> and the identities of a built-in numeric type: a constant.</summary>
    IdentityConstant,

    /// <summary>No closed C# spelling binds it.</summary>
    Unsupported,
}
