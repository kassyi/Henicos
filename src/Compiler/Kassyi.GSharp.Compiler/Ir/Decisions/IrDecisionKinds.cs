using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;

namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Single source of truth for which IR node kinds hold a decision and the sealed <see cref="IrDecision"/> type each
/// kind holds (#1561).
/// </summary>
internal static class IrDecisionKinds
{
    /// <summary>
    /// The <see cref="IrDecision"/> subtype that <paramref name="node"/> holds, or <see langword="null"/> when the
    /// node kind has no decision.
    /// </summary>
    public static Type? Of(IrNode node) => node switch
    {
        IrStatement or IrState or IrStateMachine => null,

        IrBinary => typeof(IrBinaryDecision),
        IrUnary => typeof(IrUnaryDecision),
        IrIncrement => typeof(IrStepDecision),
        IrCompoundAssignment => typeof(IrCompoundDecision),
        IrConversion => typeof(IrConversionDecision),
        IrConditional => typeof(IrConditionalDecision),
        IrConstant => typeof(IrConstantDecision),

        IrElementReference => typeof(IrElementDecision),
        IrAssignment => typeof(IrAssignmentDecision),
        IrNullTest => typeof(IrNullTestDecision),
        IrCoalesce => typeof(IrCoalesceDecision),
        IrPropertyReference => typeof(IrMemberDecision),
        IrCall => typeof(IrCallDecision),
        IrFieldReference => null,
        IrAddressOf or IrArrayCreation or IrStructCreation => null,
        IrLogical => null,
        IrDefaultValue or IrDiscard or IrInitializedObject or IrVariableReference => null,
        IrSequence => null,
        IrRuntimeCall => null,
        IrOpaqueExpression => null,

        _ => throw new InvalidOperationException(
            $"IrDecisionKinds has no entry for node kind {node.GetType().Name}.")
    };
}
