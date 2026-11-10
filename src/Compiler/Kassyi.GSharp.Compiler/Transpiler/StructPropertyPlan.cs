namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>What an accessor member function of a struct property runs.</summary>
internal enum PropertyAccessorBody : byte
{
    /// <summary>No member function: the property has no such accessor, or its body is not in this
    /// compilation.</summary>
    None,

    /// <summary>The body written in C# (<c>get =&gt; ...</c>, <c>set { ... }</c>, or the property's <c>=&gt;
    /// ...</c>).</summary>
    Written,

    /// <summary>The body the C# compiler implements for <c>get;</c>: <c>return &lt;backing field&gt;;</c>.</summary>
    ReadsBackingField,

    /// <summary>The body the C# compiler implements for <c>set;</c> / <c>init;</c>: <c>&lt;backing field&gt; =
    /// value;</c>.</summary>
    WritesBackingField
}

/// <summary>One accessor of a struct property: what it runs, and the written body when it has one.</summary>
/// <param name="Body">What the accessor runs.</param>
/// <param name="Block">The block of a <see cref="PropertyAccessorBody.Written"/> body, if it is a block.</param>
/// <param name="Expression">The expression of a <see cref="PropertyAccessorBody.Written"/> body, if it is an
/// expression.</param>
/// <param name="SyntaxTree">The tree of the written body, bound with a model of its own.</param>
internal readonly record struct PropertyAccessorPlan(
    PropertyAccessorBody Body,
    BlockSyntax? Block,
    ExpressionSyntax? Expression,
    SyntaxTree? SyntaxTree)
{
    public static readonly PropertyAccessorPlan None = new(PropertyAccessorBody.None, null, null, null);

    public static PropertyAccessorPlan Implemented(PropertyAccessorBody body) => new(body, null, null, null);

    public static PropertyAccessorPlan Written(BlockSyntax? block, ExpressionSyntax? expression, SyntaxTree tree) =>
        new(PropertyAccessorBody.Written, block, expression, tree);
}

/// <summary>
/// The accessor member functions of a struct property whose accessors G# emits
/// (<see cref="StructAbiMembers.HasAccessorFunctions"/>), decided before anything is written
/// (<see cref="StructPropertyEmitter"/> writes them).
/// </summary>
/// <param name="Getter">The getter.</param>
/// <param name="Setter">The setter or <c>init</c> accessor.</param>
/// <param name="ValueParameter">The <c>value</c> parameter of the setter, when it has one.</param>
/// <param name="IsConstGetter">Whether the getter is a <c>const</c> member function
/// (<c>PropertyAccessorCall.IsConstGetter</c>).</param>
internal readonly record struct StructPropertyPlan(
    PropertyAccessorPlan Getter,
    PropertyAccessorPlan Setter,
    IParameterSymbol? ValueParameter,
    bool IsConstGetter);
