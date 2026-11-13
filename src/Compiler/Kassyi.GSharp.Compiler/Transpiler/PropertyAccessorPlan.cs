namespace Kassyi.GSharp.Compiler.Transpiler;

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
