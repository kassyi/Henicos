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
    public override IrAssignment VisitAssignment(IrAssignment assignment) =>
        assignment.Update(
            Decide(assignment.Target, above: null, isTarget: true),
            Decide(assignment.Value, above: null, isTarget: false));

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
