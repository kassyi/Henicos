using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Bottom-up rewriter that populates <see cref="IrExpression.Decision"/> on every node listed by
/// <see cref="IrDecisionKinds"/> (#1561).
/// </summary>
internal sealed class IrDecider(IrDecisionContext context) : IrRewriter
{
    private readonly IrDecisionContext _context = context;

    /// <inheritdoc />
    public override IrExpression VisitExpression(IrExpression expression) =>
        Decide(expression, above: null, isTarget: false);

    /// <inheritdoc />
    public override IrAssignment VisitAssignment(IrAssignment assignment)
    {
        var updated = assignment.Update(
            Decide(assignment.Target, above: null, isTarget: true),
            Decide(assignment.Value, above: null, isTarget: false));
        return updated.WithDecision(IrNullDecider.DecideAssignment(updated, _context));
    }

    private IrExpression Decide(IrExpression expression, IrConversionAbove? above, bool isTarget)
    {
        switch (expression)
        {
            case IrConversion conversion:
            {
                IrConversionAbove? childAbove = conversion.IsImplicit && conversion.Conversion.Method is null
                    ? new IrConversionAbove(
                        conversion.Type,
                        conversion.Conversion.Kind is IrConversionKind.Numeric or IrConversionKind.ImplicitConstant)
                    : null;
                var updated = conversion.Update(Decide(conversion.Operand, childAbove, isTarget: false));
                return updated.WithDecision(IrNumericDecider.DecideConversion(
                    updated, above?.Target, above?.IsNumericOrConstant ?? false));
            }
            case IrAssignment assignment:
                return VisitAssignment(assignment);
            case IrCompoundAssignment compound:
            {
                var updated = compound.Update(
                    Decide(compound.Target, above: null, isTarget: true),
                    Decide(compound.Value, above: null, isTarget: false));
                return updated.WithDecision(IrNumericDecider.DecideCompound(updated, _context));
            }
            case IrIncrement increment:
            {
                var updated = increment.Update(Decide(increment.Target, above: null, isTarget: true));
                return updated.WithDecision(IrNumericDecider.DecideStep(updated));
            }
            case IrBinary binary:
            {
                var updated = (IrBinary)base.VisitExpression(binary);
                return updated.WithDecision(IrNumericDecider.DecideBinary(
                    updated, above?.Target, above?.IsNumericOrConstant ?? false, _context));
            }
            case IrUnary unary:
            {
                var updated = (IrUnary)base.VisitExpression(unary);
                return updated.WithDecision(IrNumericDecider.DecideUnary(
                    updated, above?.Target, above?.IsNumericOrConstant ?? false, _context));
            }
            case IrConditional conditional:
            {
                var updated = (IrConditional)base.VisitExpression(conditional);
                return updated.WithDecision(IrNumericDecider.DecideConditional(updated, _context));
            }
            case IrConstant constant:
                return constant.WithDecision(IrNumericDecider.DecideConstant(
                    constant, above?.Target, above?.IsNumericOrConstant ?? false));
            default:
                return DecideNullOrCall(expression);
        }
    }

    private IrExpression DecideNullOrCall(IrExpression expression)
    {
        switch (expression)
        {
            case IrElementReference element:
            {
                var updated = (IrElementReference)base.VisitExpression(element);
                return updated.WithDecision(IrNullDecider.DecideElement(updated, _context));
            }
            case IrNullTest nullTest:
            {
                var updated = (IrNullTest)base.VisitExpression(nullTest);
                return updated.WithDecision(IrNullDecider.DecideNullTest(updated, _context));
            }
            case IrCoalesce coalesce:
            {
                var updated = (IrCoalesce)base.VisitExpression(coalesce);
                return updated.WithDecision(IrNullDecider.DecideCoalesce(updated, _context));
            }
            case IrPropertyReference property:
            {
                var updated = (IrPropertyReference)base.VisitExpression(property);
                return updated.WithDecision(IrNullDecider.DecideMember(updated, _context));
            }
            case IrCall call:
            {
                var updated = (IrCall)base.VisitExpression(call);
                return updated.WithDecision(IrCallDecider.DecideCall(updated, _context));
            }
            default:
                if (IrDecisionKinds.Of(expression) is { } expectedKind)
                {
                    throw new InvalidOperationException(
                        $"IrDecider has no case for {expression.GetType().Name} (expected {expectedKind.Name}).");
                }

                return base.VisitExpression(expression);
        }
    }

    private readonly record struct IrConversionAbove(ITypeSymbol Target, bool IsNumericOrConstant);
}
