using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>The nodes directly below a node, and the role a child has for its parent.</summary>
internal static class IrNodeChildren
{
    /// <summary>
    /// The nodes directly below <paramref name="node"/>, in the order of its fields: a receiver before the arguments,
    /// the arguments in parameter order; an opaque node has none.
    /// </summary>
    public static IrNode[] Of(IrNode node) => node switch
    {
        IrBlock block => [.. block.Statements],
        IrLocalDeclaration declaration => Optional(declaration.Initializer),
        IrExpressionStatement statement => [statement.Expression],
        IrReturn @return => Optional(@return.Value),
        IrIf @if => @if.Else is { } @else ? [@if.Condition, @if.Then, @else] : [@if.Condition, @if.Then],
        IrFieldReference field => Optional(field.Receiver),
        IrElementReference element => [element.Receiver, element.Index],
        IrPropertyReference property => WithArguments(property.Receiver, property.Arguments),
        IrCall call => WithArguments(call.Receiver, call.Arguments),
        IrUnary unary => [unary.Operand],
        IrBinary binary => [binary.Left, binary.Right],
        IrLogical logical => [logical.Left, logical.Right],
        IrConditional conditional => [conditional.Condition, conditional.WhenTrue, conditional.WhenFalse],
        IrNullTest test => [test.Operand],
        IrCoalesce coalesce => [coalesce.Value, coalesce.WhenNull],
        IrConversion conversion => [conversion.Operand],
        IrAssignment assignment => [assignment.Target, assignment.Value],
        IrCompoundAssignment compound => [compound.Target, compound.Value],
        IrIncrement increment => [increment.Target],
        IrAddressOf address => [address.Operand],
        IrStructCreation creation => [.. WithArguments(null, creation.Arguments), .. creation.Initializers],
        IrArrayCreation array => [array.Length, .. array.Elements],
        IrSequence sequence => [.. sequence.SideEffects, sequence.Value],
        _ => []
    };

    /// <summary>Whether <paramref name="predicate"/> holds for <paramref name="node"/> or a node below it; stops at
    /// the first match.</summary>
    public static bool Exists(IrNode node, Func<IrNode, bool> predicate) =>
        predicate(node) ||
        Of(node)
            .AsStackEnumerable()
            .Any(predicate, static (child, match) => Exists(child, match));

    /// <summary><paramref name="node"/> and every node below it, depth first.</summary>
    public static ImmutableArray<IrNode> DescendantsAndSelf(IrNode node)
    {
        var nodes = ImmutableArray.CreateBuilder<IrNode>();
        AddDescendantsAndSelf(node, nodes);
        return nodes.ToImmutable();
    }

    private static void AddDescendantsAndSelf(IrNode node, ImmutableArray<IrNode>.Builder nodes)
    {
        nodes.Add(node);
        foreach (var child in Of(node))
        {
            AddDescendantsAndSelf(child, nodes);
        }
    }

    /// <summary>Whether <paramref name="child"/> is the location that <paramref name="parent"/> writes.</summary>
    public static bool IsWriteTarget(IrNode? parent, IrNode child) => parent switch
    {
        IrAssignment assignment => assignment.Target == child,
        IrCompoundAssignment compound => compound.Target == child,
        IrIncrement increment => increment.Target == child,
        _ => false
    };

    /// <summary>Whether <paramref name="child"/> is the value of an <see langword="out"/> argument of the
    /// parent.</summary>
    public static bool IsOutArgument(IrNode? parent, IrNode child) => parent switch
    {
        IrCall call => IsOutArgument(call.Arguments, child),
        IrPropertyReference property => IsOutArgument(property.Arguments, child),
        IrStructCreation creation => IsOutArgument(creation.Arguments, child),
        _ => false
    };

    private static bool IsOutArgument(ImmutableArray<IrArgument> arguments, IrNode child) =>
        arguments
            .AsStackEnumerable()
            .Any(child, static (argument, value) =>
                argument.Value == value && argument.Parameter.RefKind == RefKind.Out);

    private static IrNode[] Optional(IrNode? node) => node is null ? [] : [node];

    private static IrNode[] WithArguments(IrExpression? receiver, ImmutableArray<IrArgument> arguments)
    {
        var offset = receiver is null ? 0 : 1;
        var children = new IrNode[arguments.Length + offset];
        if (receiver is not null)
        {
            children[0] = receiver;
        }

        for (var index = 0; index < arguments.Length; index++)
        {
            children[index + offset] = arguments[index].Value;
        }

        return children;
    }
}
