using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Computes the numeric, constant-folding, and error-site decisions for numeric IR expression nodes by calling the
/// shared helpers in <see cref="IntegerSemantics"/>, <see cref="FloatingPointRemainder"/>,
/// <see cref="ArithmeticConversions"/>, <see cref="ManagedDivision"/>, and <see cref="ConstantAtUse"/> (#1561).
/// </summary>
internal static class IrNumericDecider
{
    /// <summary>Decides the numeric properties of <paramref name="binary"/>.</summary>
    public static IrBinaryDecision DecideBinary(IrBinary binary, ITypeSymbol? conversionTarget,
        bool isNumericOrConstant, IrDecisionContext context)
    {
        var temporaries = context.TemporaryInitializers;
        var facts = IrNumericFacts.Binary(binary, temporaries);
        var managedDivision = ManagedDivision.PlanBinary(
            binary.Operator.Kind,
            facts.IsBuiltIn,
            binary.Operator.IsChecked,
            binary.ConstantValue.HasValue,
            binary.Type,
            facts.Left,
            facts.Right);

        if (IntegerSemantics.GetConstant(in facts, binary.ConstantValue) is var (constType, constValue))
        {
            var typeAtUse = ConstantAtUse.GetTypeAtUse(constType, conversionTarget, isNumericOrConstant);
            return new IrBinaryDecision(new IrFoldedConstant(typeAtUse, constValue), null, null, null, 0, null, null,
                false, false, managedDivision);
        }

        if (FloatingPointRemainder.GetHelper(binary.Operator.Kind, facts.IsBuiltIn, binary.Type) is { } remainder)
        {
            return new IrBinaryDecision(null, remainder, null, null, 0, null, null, false, false, managedDivision);
        }

        if (IntegerSemantics.GetBinaryHelper(in facts) is { } helper)
        {
            var site = IntegerSemantics.TakesErrorSite(helper)
                ? new IrErrorSite(binary.Source, DivisorCheck.TrapKind, DivisorCheck.Message)
                : (IrErrorSite?)null;
            return new IrBinaryDecision(null, null, helper, site, 0, null, null, false, false, managedDivision);
        }

        var shiftMask = IntegerSemantics.GetShiftMask(in facts);
        if (shiftMask != 0)
        {
            return new IrBinaryDecision(null, null, null, null, shiftMask, null, null, false, false, managedDivision);
        }

        if (IntegerSemantics.TryGetNarrowEnumResultType(in facts, out var narrowEnumType))
        {
            return new IrBinaryDecision(null, null, null, null, 0, narrowEnumType, null, false, false, managedDivision);
        }

        var leftSource = IrNumericFacts.SourceType(binary.Left, temporaries);
        var rightSource = IrNumericFacts.SourceType(binary.Right, temporaries);
        var promotedType = ArithmeticConversions.TryGetPromotedType(
            binary.Operator.Kind,
            facts.IsBuiltIn,
            binary.Left.Type,
            binary.Right.Type,
            leftSource,
            rightSource,
            out var promoted)
            ? promoted
            : null;
        var convertsLeft = promotedType is not null && ArithmeticConversions.IsConverted(leftSource, promotedType);
        var convertsRight = promotedType is not null && ArithmeticConversions.IsConverted(rightSource, promotedType);

        return new IrBinaryDecision(null, null, null, null, 0, null, promotedType, convertsLeft, convertsRight,
            managedDivision);
    }

    /// <summary>Decides the numeric properties of <paramref name="unary"/>.</summary>
    public static IrUnaryDecision DecideUnary(IrUnary unary, ITypeSymbol? conversionTarget,
        bool isNumericOrConstant, IrDecisionContext context)
    {
        var temporaries = context.TemporaryInitializers;
        var facts = IrNumericFacts.Unary(unary, temporaries);

        if (IsNegatedNumericLiteral(unary) &&
            unary.ConstantValue is { HasValue: true, Value: { } literalValue })
        {
            var typeAtUse = ConstantAtUse.GetTypeAtUse(unary.Type, conversionTarget, isNumericOrConstant);
            return new IrUnaryDecision(new IrFoldedConstant(typeAtUse, literalValue), null, null, null, false);
        }

        if (IntegerSemantics.GetConstant(in facts, unary.ConstantValue) is var (constType, constValue))
        {
            var typeAtUse = ConstantAtUse.GetTypeAtUse(constType, conversionTarget, isNumericOrConstant);
            return new IrUnaryDecision(new IrFoldedConstant(typeAtUse, constValue), null, null, null, false);
        }

        if (IntegerSemantics.GetNegationHelper(in facts) is { } helper)
        {
            return new IrUnaryDecision(null, helper, null, null, false);
        }

        if (IntegerSemantics.TryGetNarrowEnumResultType(in facts, out var narrowEnumType))
        {
            return new IrUnaryDecision(null, null, narrowEnumType, null, false);
        }

        var sourceType = IrNumericFacts.SourceType(unary.Operand, temporaries);
        var promotedType = ArithmeticConversions.TryGetPromotedType(
            unary.Operator.Kind,
            facts.IsBuiltIn,
            unary.Type,
            sourceType,
            out var promoted)
            ? promoted
            : null;
        var convertsOperand = promotedType is not null && ArithmeticConversions.IsConverted(sourceType, promotedType);

        return new IrUnaryDecision(null, null, null, promotedType, convertsOperand);
    }

    /// <summary>Decides the step helpers and narrowing of <paramref name="increment"/>.</summary>
    public static IrStepDecision DecideStep(IrIncrement increment)
    {
        var facts = IrNumericFacts.Step(increment);
        return new IrStepDecision(
            IntegerSemantics.GetStepHelper(in facts, undo: false),
            IntegerSemantics.GetStepHelper(in facts, undo: true),
            IntegerSemantics.GetStepNarrowing(in facts));
    }

    /// <summary>
    /// Decides the helper, site, shift mask, narrowing, and operator type of <paramref name="compound"/>.
    /// </summary>
    public static IrCompoundDecision DecideCompound(IrCompoundAssignment compound, IrDecisionContext context)
    {
        var temporaries = context.TemporaryInitializers;
        var facts = IrNumericFacts.Compound(compound, temporaries);
        var operatorType = IntegerSemantics.GetCompoundOperatorType(
            compound.Operator.Kind,
            compound.Target.Type,
            compound.Value.Type);
        var remainder = FloatingPointRemainder.GetCompoundHelper(
            compound.Operator.Kind,
            facts.IsBuiltIn,
            compound.Target.Type);
        var helper = IntegerSemantics.GetCompoundHelper(in facts);
        var shiftMask = helper is null ? IntegerSemantics.GetCompoundShiftMask(in facts) : 0;
        var site = helper is not null && IntegerSemantics.TakesErrorSite(helper)
            ? new IrErrorSite(compound.Source, DivisorCheck.TrapKind, DivisorCheck.Message)
            : (IrErrorSite?)null;
        var narrowing = IntegerSemantics.GetCompoundNarrowing(in facts);
        var convertsValue = remainder is not null &&
            ArithmeticConversions.IsConverted(IrNumericFacts.SourceType(compound.Value, temporaries),
                compound.Target.Type);

        return new IrCompoundDecision(remainder, helper, site, shiftMask, narrowing, operatorType, convertsValue);
    }

    /// <summary>
    /// Decides the form, helper, cast requirement, and folded constant of <paramref name="conversion"/>.
    /// </summary>
    public static IrConversionDecision DecideConversion(IrConversion conversion, ITypeSymbol? conversionTarget,
        bool isNumericOrConstant)
    {
        if (IsUnsupportedConversion(conversion))
        {
            return new IrConversionDecision(IrConversionForm.Unsupported, null, false, null);
        }

        if (!conversion.IsImplicit &&
            IntegerSemantics.GetConstant(conversion.Type, conversion.ConstantValue, false) is
                var (constType, constValue))
        {
            var typeAtUse = ConstantAtUse.GetTypeAtUse(constType, conversionTarget, isNumericOrConstant);
            return new IrConversionDecision(
                IrConversionForm.Folded,
                null,
                false,
                new IrFoldedConstant(typeAtUse, constValue));
        }

        if (!conversion.ConstantValue.HasValue &&
            IntegerSemantics.GetConversionHelper(conversion.Operand.Type, conversion.Type, out var needsCast) is
                { } helper)
        {
            return new IrConversionDecision(IrConversionForm.FloatToInteger, helper, needsCast, null);
        }

        return new IrConversionDecision(
            conversion.IsImplicit ? IrConversionForm.Transparent : IrConversionForm.Cast,
            null,
            false,
            null);
    }

    /// <summary>Decides the promoted branch type and conversions of <paramref name="conditional"/>.</summary>
    public static IrConditionalDecision DecideConditional(IrConditional conditional, IrDecisionContext context)
    {
        var temporaries = context.TemporaryInitializers;
        var trueSource = IrNumericFacts.SourceType(conditional.WhenTrue, temporaries);
        var falseSource = IrNumericFacts.SourceType(conditional.WhenFalse, temporaries);
        var promotedType = ArithmeticConversions.TryGetPromotedConditionalType(
            conditional.IsRef,
            conditional.Type,
            trueSource,
            falseSource,
            out var promoted)
            ? promoted
            : null;

        var convertsTrue = promotedType is not null && ArithmeticConversions.IsConverted(trueSource, promotedType);
        var convertsFalse = promotedType is not null && ArithmeticConversions.IsConverted(falseSource, promotedType);
        return new IrConditionalDecision(promotedType, convertsTrue, convertsFalse);
    }

    /// <summary>Decides the type C# gives <paramref name="constant"/> where it is used.</summary>
    public static IrConstantDecision DecideConstant(IrConstant constant, ITypeSymbol? conversionTarget,
        bool isNumericOrConstant) =>
        new(ConstantAtUse.GetTypeAtUse(constant.Type, conversionTarget, isNumericOrConstant));

    private static bool IsNegatedNumericLiteral(IrUnary unary) =>
        unary.Operator.Kind == UnaryOperatorKind.Minus &&
        unary.Operand is IrConstant { LiteralText: not null, Parentheses: 0 } constant &&
        constant.Type.SpecialType != SpecialType.System_Char;

    private static bool IsUnsupportedConversion(IrConversion conversion) =>
        conversion.Conversion.Method is not null ||
        conversion.Conversion.Kind is IrConversionKind.Nullable or IrConversionKind.Pointer or
            IrConversionKind.Boxing or IrConversionKind.Unboxing ||
        conversion.Type.TypeKind == TypeKind.Delegate ||
        conversion.Operand.Type.TypeKind == TypeKind.Delegate;
}
