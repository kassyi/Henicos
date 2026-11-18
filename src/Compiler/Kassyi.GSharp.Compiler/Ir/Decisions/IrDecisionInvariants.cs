using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Checks the invariants of a decided IR tree: I28 a node of a kind that has a decision holds none; I29 it holds the
/// decision of another kind or a non-decided node holds one; I30 a decision breaks the rule of its own fields (#1561).
/// </summary>
internal static class IrDecisionInvariants
{
    /// <summary>The violations of a decided tree rooted at <paramref name="root"/>; empty when all hold.</summary>
    public static ImmutableArray<string> Verify(IrNode root)
    {
        var violations = ImmutableArray.CreateBuilder<string>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(root))
        {
            var expected = IrDecisionKinds.Of(node);
            if (node is not IrExpression expression)
            {
                continue;
            }

            if (expected is null)
            {
                if (expression.Decision is { } unexpected)
                {
                    Add(violations, "I29", node.Source,
                        $"{node.GetType().Name}: unexpected decision {unexpected.GetType().Name}");
                }

                continue;
            }

            if (expression.Decision is not { } decision)
            {
                Add(violations, "I28", node.Source, $"{node.GetType().Name}: missing {expected.Name}");
                continue;
            }

            if (decision.GetType() != expected)
            {
                Add(violations, "I29", node.Source,
                    $"{node.GetType().Name}: expected {expected.Name}, found {decision.GetType().Name}");
                continue;
            }

            CheckFields(expression, decision, violations);
        }

        return violations.ToImmutable();
    }

    private static void CheckFields(IrExpression node, IrDecision decision,
        ImmutableArray<string>.Builder violations)
    {
        switch (node, decision)
        {
            case (IrBinary binary, IrBinaryDecision binaryDecision):
                CheckBinary(binary, binaryDecision, violations);
                break;
            case (IrUnary unary, IrUnaryDecision unaryDecision):
                CheckUnary(unary, unaryDecision, violations);
                break;
            case (IrCompoundAssignment compound, IrCompoundDecision compoundDecision):
                CheckCompound(compound, compoundDecision, violations);
                break;
            case (IrConversion conversion, IrConversionDecision conversionDecision):
                CheckConversion(conversion, conversionDecision, violations);
                break;
            case (IrConditional conditional, IrConditionalDecision conditionalDecision):
                CheckConditional(conditional, conditionalDecision, violations);
                break;
        }
    }

    private static void CheckBinary(IrBinary node, IrBinaryDecision d, ImmutableArray<string>.Builder violations)
    {
        if (d.Site is not null && (d.Helper is null || !IntegerSemantics.TakesErrorSite(d.Helper)))
        {
            AddI30(violations, node, "Site requires a Helper that takes an error site");
        }

        if (d.Folded is not null && !node.ConstantValue.HasValue)
        {
            AddI30(violations, node, "Folded requires a constant value on the node");
        }

        if ((d.ConvertsLeft || d.ConvertsRight) && d.PromotedType is null)
        {
            AddI30(violations, node, "ConvertsLeft/ConvertsRight requires PromotedType");
        }

        if (d.ShiftMask is not (0 or 31 or 63))
        {
            AddI30(violations, node, "ShiftMask must be 0, 31, or 63");
        }

        if (d.Folded is not null && (d.FloatingRemainder is not null || d.Helper is not null ||
                                     d.ShiftMask != 0 || d.NarrowEnumType is not null || d.PromotedType is not null))
        {
            AddI30(violations, node,
                "Folded excludes FloatingRemainder, Helper, ShiftMask, NarrowEnumType, PromotedType");
        }

        if (d.Helper is not null && d.ShiftMask != 0)
        {
            AddI30(violations, node, "Helper excludes ShiftMask");
        }
    }

    private static void CheckUnary(IrUnary node, IrUnaryDecision d, ImmutableArray<string>.Builder violations)
    {
        if (d.ConvertsOperand && d.PromotedType is null)
        {
            AddI30(violations, node, "ConvertsOperand requires PromotedType");
        }

        if (d.Folded is not null && !node.ConstantValue.HasValue)
        {
            AddI30(violations, node, "Folded requires a constant value on the node");
        }

        if (d.Folded is not null &&
            (d.Helper is not null || d.NarrowEnumType is not null || d.PromotedType is not null))
        {
            AddI30(violations, node, "Folded excludes Helper, NarrowEnumType, PromotedType");
        }
    }

    private static void CheckCompound(IrCompoundAssignment node, IrCompoundDecision d,
        ImmutableArray<string>.Builder violations)
    {
        if (d.Site is not null && (d.Helper is null || !IntegerSemantics.TakesErrorSite(d.Helper)))
        {
            AddI30(violations, node, "Site requires a Helper that takes an error site");
        }

        if (d.ShiftMask is not (0 or 31 or 63))
        {
            AddI30(violations, node, "ShiftMask must be 0, 31, or 63");
        }

        if (d.Helper is not null && d.ShiftMask != 0)
        {
            AddI30(violations, node, "Helper excludes ShiftMask");
        }

        if (d.ConvertsValue && d.FloatingRemainder is null)
        {
            AddI30(violations, node, "ConvertsValue requires FloatingRemainder");
        }
    }

    private static void CheckConversion(IrConversion node, IrConversionDecision d,
        ImmutableArray<string>.Builder violations)
    {
        if ((d.Form == IrConversionForm.FloatToInteger) != (d.Helper is not null))
        {
            AddI30(violations, node, "FloatToInteger iff Helper is non-null");
        }

        if ((d.Form == IrConversionForm.Folded) != (d.Folded is not null))
        {
            AddI30(violations, node, "Folded iff Folded value is non-null");
        }

        if (d.NeedsCast && d.Form != IrConversionForm.FloatToInteger)
        {
            AddI30(violations, node, "NeedsCast requires FloatToInteger");
        }

        if (d.Form == IrConversionForm.Transparent && !node.IsImplicit)
        {
            AddI30(violations, node, "Transparent requires IsImplicit");
        }
    }

    private static void CheckConditional(IrConditional node, IrConditionalDecision d,
        ImmutableArray<string>.Builder violations)
    {
        if ((d.ConvertsWhenTrue || d.ConvertsWhenFalse) && d.PromotedType is null)
        {
            AddI30(violations, node, "ConvertsWhenTrue/ConvertsWhenFalse requires PromotedType");
        }
    }

    private static void AddI30(ImmutableArray<string>.Builder violations, IrExpression node, string rule) =>
        Add(violations, "I30", node.Source, $"{node.GetType().Name}: {rule}");

    private static void Add(ImmutableArray<string>.Builder violations, string code, IrSource source, string text) =>
        violations.Add($"{code} at {source.Start}..{source.Start + source.Length}: {text}");
}
