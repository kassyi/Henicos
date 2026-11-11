using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// The operands of an IR expression in the C# order of evaluation, as <see cref="IrSpillPlanner"/> walks them, and
/// their effects.
/// </summary>
internal static class IrOperandLists
{
    /// <summary>
    /// The operands of an expression whose operands are evaluated in order and then combined, with the role a spill of
    /// an earlier operand has; <see langword="null"/> for the other kinds (calls, conditionals, compound assignments,
    /// leaves), which the planner handles itself.
    /// </summary>
    public static (IrExpression[] Operands, IrSpillRole Role)? Of(IrExpression expression) => expression switch
    {
        IrBinary binary => ([binary.Left, binary.Right], IrSpillRole.LeftOperand),
        IrUnary unary => ([unary.Operand], IrSpillRole.EarlierOperand),
        IrConversion conversion => ([conversion.Operand], IrSpillRole.EarlierOperand),
        IrNullTest test => ([test.Operand], IrSpillRole.EarlierOperand),
        IrFieldReference { Receiver: { } receiver } => ([receiver], IrSpillRole.EarlierOperand),
        IrElementReference element => ([element.Receiver, element.Index], IrSpillRole.EarlierOperand),
        IrPropertyReference property => (Of(property.Receiver, property.Arguments), IrSpillRole.EarlierOperand),
        IrAssignment { IsRef: false } assignment => ([assignment.Target, assignment.Value], IrSpillRole.TargetPart),
        IrIncrement increment => ([increment.Target], IrSpillRole.TargetPart),
        IrStructCreation creation => (Of(creation), IrSpillRole.EarlierOperand),
        IrArrayCreation array => ([array.Length, .. array.Elements], IrSpillRole.EarlierOperand),
        _ => null
    };

    /// <summary>A receiver, then the arguments in the C# order of evaluation.</summary>
    public static IrExpression[] Of(IrExpression? receiver, ImmutableArray<IrArgument> arguments)
    {
        var offset = receiver is null ? 0 : 1;
        var operands = new IrExpression[arguments.Length + offset];
        if (receiver is not null)
        {
            operands[0] = receiver;
        }

        // EvaluationIndex numbers the arguments 0..n-1 in the order C# evaluates them
        foreach (var argument in arguments)
        {
            operands[argument.EvaluationIndex + offset] = argument.Value;
        }

        return operands;
    }

    /// <summary>
    /// How many of the operands of <paramref name="call"/> are its receiver: the receiver of an instance method, or the
    /// receiver of an extension method called on it (<see cref="IrArgumentKind.ExtensionReceiver"/>).
    /// </summary>
    public static int ReceiverCount(IrCall call) =>
        call.Receiver is not null ||
        call.Arguments
            .AsStackEnumerable()
            .Any(static argument => argument.Kind == IrArgumentKind.ExtensionReceiver)
            ? 1
            : 0;

    /// <summary>What evaluating each operand as its holder uses it reads and writes.</summary>
    public static ExpressionEffects[] EffectsOf(IrExpression[] operands, Compilation compilation)
    {
        var effects = new ExpressionEffects[operands.Length];
        for (var index = 0; index < operands.Length; index++)
        {
            effects[index] = IrStayInPlace.EffectsOf(operands[index], compilation);
        }

        return effects;
    }

    /// <summary>The union of <c>effects[start..end)</c>.</summary>
    public static ExpressionEffects Union(ExpressionEffects[] effects, int start, int end)
    {
        var union = default(ExpressionEffects);
        for (var index = start; index < end; index++)
        {
            union = union.Union(effects[index]);
        }

        return union;
    }

    // The arguments of a creation, then the values of its initializer (each target is a member of the new value)
    private static IrExpression[] Of(IrStructCreation creation)
    {
        var arguments = Of(null, creation.Arguments);
        var operands = new IrExpression[arguments.Length + creation.Initializers.Length];
        arguments.CopyTo(operands, 0);
        for (var index = 0; index < creation.Initializers.Length; index++)
        {
            operands[arguments.Length + index] = creation.Initializers[index].Value;
        }

        return operands;
    }
}
