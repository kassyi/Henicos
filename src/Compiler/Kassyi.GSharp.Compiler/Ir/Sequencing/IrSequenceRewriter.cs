using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// The rewriter of <see cref="IrSequencer"/>: plans each form from its original operands, rewrites the operands, then
/// builds the sequence over the rewritten ones.
/// </summary>
/// <remarks>
/// The effects that decide a plan are those of the operands as written, as the syntax path reads the syntax before it
/// writes anything: a rewritten operand holds temporaries and sequences that read and throw differently. Where the
/// syntax path does not write a part of a form on its own (the element target of an assignment, the parts of a target
/// a helper spells twice), the form rewrites the parts itself.
/// </remarks>
internal sealed class IrSequenceRewriter(IrSequenceContext context) : IrRewriter
{
    private readonly IrSequenceContext _context = context;

    /// <inheritdoc />
    public override IrExpression VisitExpression(IrExpression expression) => expression switch
    {
        IrBinary binary => VisitBinary(binary),
        IrElementReference element => VisitElement(element),
        IrAssignment assignment => VisitSimpleAssignment(assignment),
        IrCompoundAssignment compound => VisitCompound(compound),
        IrIncrement increment => VisitIncrement(increment),
        IrStructCreation creation => VisitCreation(creation),
        _ => base.VisitExpression(expression)
    };

    /// <inheritdoc />
    public override IrExpression VisitCall(IrCall call)
    {
        var plan = IrCallSequencing.Plan(call, _context);
        var rewritten = base.VisitCall(call) as IrCall ??
                        throw new InvalidOperationException("A call was rewritten into another node.");
        return plan.IsSequenced ? IrCallSequencing.Apply(call, plan, rewritten, _context) : rewritten;
    }

    private IrExpression VisitCreation(IrStructCreation creation)
    {
        var plan = IrStructCreationSequencing.Plan(creation, _context);
        var rewritten = base.VisitExpression(creation) as IrStructCreation ??
                        throw new InvalidOperationException("A struct creation was rewritten into another node.");
        return plan.IsSequenced ? IrStructCreationSequencing.Apply(creation, plan, rewritten) : rewritten;
    }

    private IrExpression VisitBinary(IrBinary binary)
    {
        var plan = IrBinarySequencing.Plan(binary, _context);
        var left = VisitExpression(binary.Left);
        var right = VisitExpression(binary.Right);
        return plan.IsSequenced
            ? IrBinarySequencing.Apply(binary, plan, left, right)
            : binary.Update(left, right);
    }

    private IrExpression VisitElement(IrElementReference element)
    {
        var plan = IrElementSequencing.Plan(element, _context);
        var receiver = VisitExpression(element.Receiver);
        var index = VisitExpression(element.Index);
        return plan.IsSequenced
            ? IrElementSequencing.Apply(element, receiver, index)
            : element.Update(receiver, index);
    }

    // A simple assignment; the assignments of an object initializer go through VisitAssignment and stay as they are
    private IrExpression VisitSimpleAssignment(IrAssignment assignment)
    {
        var plan = IrAssignmentSequencing.Plan(assignment, _context);
        return plan.IsSequenced
            ? IrAssignmentSequencing.Apply(assignment, plan, VisitExpression)
            : base.VisitAssignment(assignment);
    }

    private IrExpression VisitCompound(IrCompoundAssignment compound)
    {
        var plan = IrCompoundSequencing.Plan(compound, _context);
        if (plan.IsSequenced)
        {
            return IrCompoundSequencing.Apply(compound, plan, VisitExpression(compound.Target),
                VisitExpression(compound.Value));
        }

        var parts = IrTargetPartSequencing.AppliesTo(compound)
            ? IrTargetPartSequencing.Plan(compound.Target, _context)
            : IrSequencePlan.None;
        if (!parts.IsSequenced)
        {
            return compound.Update(VisitExpression(compound.Target), VisitExpression(compound.Value));
        }

        var builder = new IrSequenceBuilder();
        var target = IrTargetPartSequencing.Apply(compound.Target, parts, builder, VisitExpression, _context);
        return builder.Build(compound.Update(target, VisitExpression(compound.Value)), compound);
    }

    private IrExpression VisitIncrement(IrIncrement increment)
    {
        var parts = IrTargetPartSequencing.AppliesTo(increment)
            ? IrTargetPartSequencing.Plan(increment.Target, _context)
            : IrSequencePlan.None;
        if (!parts.IsSequenced)
        {
            return increment.Update(VisitExpression(increment.Target));
        }

        var builder = new IrSequenceBuilder();
        var target = IrTargetPartSequencing.Apply(increment.Target, parts, builder, VisitExpression, _context);
        return builder.Build(increment.Update(target), increment);
    }
}
