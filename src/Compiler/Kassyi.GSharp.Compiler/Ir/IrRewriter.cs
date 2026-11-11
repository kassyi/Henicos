using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// The base of a pass that rewrites an IR tree bottom-up: each node is rebuilt from its rewritten children through its
/// <c>Update</c>, which keeps the node itself when no child changed, so an unchanged subtree stays the same instances.
/// </summary>
/// <remarks>
/// A pass overrides the node kinds it changes and calls the base to rewrite the children first. Children are visited
/// in the order of their fields (<see cref="IrNodeChildren.Of"/>): a receiver before the arguments, the arguments in
/// parameter order. That is not the C# order of evaluation: a pass that needs it reads
/// <see cref="IrArgument.EvaluationIndex"/>, which differs for named arguments. Leaves (constants, variables) are
/// returned as they are; opaque nodes go to <see cref="VisitOpaqueStatement"/> and <see cref="VisitOpaqueExpression"/>.
/// A node kind with children and no case here is an internal error. A pass that must put statements before the one
/// it rewrites returns an <see cref="Expressions.IrSequence"/>, which the split pass lifts to the enclosing block
/// afterwards, as Roslyn lifts a spill sequence.
/// </remarks>
internal abstract class IrRewriter
{
    /// <summary>Rewrites the body of <paramref name="method"/>; the method itself when the body is unchanged.</summary>
    public IrMethod RewriteMethod(IrMethod method)
    {
        var body = VisitBlock(method.Body);
        return body == method.Body ? method : new IrMethod(method.Symbol, method.Parameters, method.This, body,
            method.Source);
    }

    /// <summary>Rewrites a statement.</summary>
    public virtual IrStatement VisitStatement(IrStatement statement) => statement switch
    {
        IrBlock block => VisitBlock(block),
        IrLocalDeclaration declaration => declaration.Update(VisitOptional(declaration.Initializer)),
        IrExpressionStatement expression => expression.Update(VisitExpression(expression.Expression)),
        IrReturn @return => @return.Update(VisitOptional(@return.Value)),
        IrIf @if => @if.Update(VisitExpression(@if.Condition), VisitStatement(@if.Then),
            @if.Else is { } @else ? VisitStatement(@else) : null),
        IrOpaqueStatement opaque => VisitOpaqueStatement(opaque),
        _ => throw Unknown(statement)
    };

    /// <summary>Rewrites a block.</summary>
    public virtual IrBlock VisitBlock(IrBlock block) => block.Update(VisitList(block.Statements, VisitStatement));

    /// <summary>Rewrites an expression.</summary>
    public virtual IrExpression VisitExpression(IrExpression expression) => expression switch
    {
        IrCall call => VisitCall(call),
        IrFieldReference field => field.Update(VisitOptional(field.Receiver)),
        IrElementReference element => element.Update(VisitExpression(element.Receiver),
            VisitExpression(element.Index)),
        IrPropertyReference property => property.Update(VisitOptional(property.Receiver),
            VisitArguments(property.Arguments)),
        IrUnary unary => unary.Update(VisitExpression(unary.Operand)),
        IrBinary binary => binary.Update(VisitExpression(binary.Left), VisitExpression(binary.Right)),
        IrLogical logical => logical.Update(VisitExpression(logical.Left), VisitExpression(logical.Right)),
        IrConditional conditional => conditional.Update(VisitExpression(conditional.Condition),
            VisitExpression(conditional.WhenTrue), VisitExpression(conditional.WhenFalse)),
        IrNullTest test => test.Update(VisitExpression(test.Operand)),
        IrCoalesce coalesce => coalesce.Update(VisitExpression(coalesce.Value), VisitExpression(coalesce.WhenNull)),
        IrConversion conversion => conversion.Update(VisitExpression(conversion.Operand)),
        IrAssignment assignment => VisitAssignment(assignment),
        IrCompoundAssignment compound => compound.Update(VisitExpression(compound.Target),
            VisitExpression(compound.Value)),
        IrIncrement increment => increment.Update(VisitExpression(increment.Target)),
        IrAddressOf address => address.Update(VisitExpression(address.Operand)),
        IrStructCreation creation => creation.Update(VisitArguments(creation.Arguments),
            VisitList(creation.Initializers, VisitAssignment)),
        IrArrayCreation array => array.Update(VisitExpression(array.Length),
            VisitList(array.Elements, VisitExpression)),
        IrSequence sequence => sequence.Update(VisitList(sequence.SideEffects, VisitStatement),
            VisitExpression(sequence.Value)),
        IrOpaqueExpression opaque => VisitOpaqueExpression(opaque),
        IrConstant or IrVariableReference or IrDefaultValue or IrDiscard or IrInitializedObject => expression,
        _ => throw Unknown(expression)
    };

    /// <summary>Rewrites an opaque statement; the statement itself by default.</summary>
    public virtual IrStatement VisitOpaqueStatement(IrOpaqueStatement statement) => statement;

    /// <summary>Rewrites an opaque expression; the expression itself by default.</summary>
    public virtual IrExpression VisitOpaqueExpression(IrOpaqueExpression expression) => expression;

    /// <summary>Rewrites a call: its receiver and arguments, keeping its classification.</summary>
    public virtual IrExpression VisitCall(IrCall call) =>
        call.Update(VisitOptional(call.Receiver), VisitArguments(call.Arguments), call.Classification);

    /// <summary>Rewrites a simple assignment, also one of a struct initializer.</summary>
    public virtual IrAssignment VisitAssignment(IrAssignment assignment) =>
        assignment.Update(VisitExpression(assignment.Target), VisitExpression(assignment.Value));

    /// <summary>Rewrites the values of <paramref name="arguments"/>; the same array when none changed.</summary>
    protected ImmutableArray<IrArgument> VisitArguments(ImmutableArray<IrArgument> arguments)
    {
        ImmutableArray<IrArgument>.Builder? rewritten = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            var value = VisitExpression(argument.Value);
            if (rewritten is null && value == argument.Value)
            {
                continue;
            }

            rewritten ??= CopyPrefix(arguments, index);
            rewritten.Add(new IrArgument(argument.Parameter, value, argument.Kind, argument.EvaluationIndex));
        }

        return rewritten?.MoveToImmutable() ?? arguments;
    }

    /// <summary>Rewrites an optional expression.</summary>
    protected IrExpression? VisitOptional(IrExpression? expression) =>
        expression is null ? null : VisitExpression(expression);

    // The nodes of a list, rewritten; the same array when none changed
    private static ImmutableArray<T> VisitList<T>(ImmutableArray<T> nodes, Func<T, T> visit) where T : IrNode
    {
        ImmutableArray<T>.Builder? rewritten = null;
        for (var index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            var visited = visit(node);
            if (rewritten is null && visited == node)
            {
                continue;
            }

            rewritten ??= CopyPrefix(nodes, index);
            rewritten.Add(visited);
        }

        return rewritten?.MoveToImmutable() ?? nodes;
    }

    private static InvalidOperationException Unknown(IrNode node) =>
        new($"The IR rewriter has no case for a node of kind {node.GetType().Name}.");

    // A builder of the length of items that holds its first count items
    private static ImmutableArray<T>.Builder CopyPrefix<T>(ImmutableArray<T> items, int count)
    {
        var builder = ImmutableArray.CreateBuilder<T>(items.Length);
        for (var index = 0; index < count; index++)
        {
            builder.Add(items[index]);
        }

        return builder;
    }
}
