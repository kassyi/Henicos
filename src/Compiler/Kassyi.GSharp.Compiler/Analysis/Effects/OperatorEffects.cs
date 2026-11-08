namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The effects of operators for <see cref="ExpressionEffects"/>: user-defined conversions, prefix and postfix
/// operators, increments and assignments.
/// </summary>
internal static class OperatorEffects
{
    // A user-defined conversion of the value (implicit, or the operator of a cast) is a call
    public static void AddConversions(ref EffectWalker walker, ExpressionSyntax expression)
    {
        var model = walker.Model;
        if (model.GetSafeConversion(expression) is { IsUserDefined: true, MethodSymbol: { } conversion })
        {
            AddConversionCall(ref walker, conversion);
        }

        if (expression is CastExpressionSyntax &&
            model.GetSymbol(expression) is IMethodSymbol { MethodKind: MethodKind.Conversion } castOperator)
        {
            AddConversionCall(ref walker, castOperator);
        }
    }

    // A conversion operator reads what it may and writes what DeviceFunctionEffects says
    private static void AddConversionCall(ref EffectWalker walker, IMethodSymbol conversion)
    {
        walker.Reads.Memory = true;
        if (walker.MayWriteMemory(conversion))
        {
            walker.Writes.Memory = true;
        }
    }

    public static void AddPostfix(ref EffectWalker walker, PostfixUnaryExpressionSyntax postfix)
    {
        if (postfix.IsKind(SyntaxKind.PostIncrementExpression, SyntaxKind.PostDecrementExpression))
        {
            AddIncrement(ref walker, postfix.Operand);
        }
        else
        {
            walker.AddValue(postfix.Operand);
        }
    }

    public static void AddPrefix(ref EffectWalker walker, PrefixUnaryExpressionSyntax prefix)
    {
        switch (prefix.Kind())
        {
            case SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression:
                AddIncrement(ref walker, prefix.Operand);
                return;
            case SyntaxKind.PointerIndirectionExpression:
                walker.AddValue(prefix.Operand);
                walker.Reads.Memory = true;
                return;
            case SyntaxKind.AddressOfExpression:
                walker.AddLocation(prefix.Operand);
                return;
            default:
                walker.AddValue(prefix.Operand);
                return;
        }
    }

    private static void AddIncrement(ref EffectWalker walker, ExpressionSyntax operand)
    {
        var root = walker.AddLocation(operand);
        walker.AddRead(root);
        walker.AddWrite(root);
        if (walker.Model.GetSymbol(operand) is not IPropertySymbol property ||
            !MemberEffects.IsComputedUserProperty(property))
        {
            return;
        }

        walker.Reads.Memory = true;
        if (walker.MayWriteMemory(property.GetMethod) || walker.MayWriteMemory(property.SetMethod))
        {
            walker.Writes.Memory = true;
        }
    }

    public static void AddAssignment(ref EffectWalker walker, AssignmentExpressionSyntax assignment)
    {
        if (assignment.Left is TupleExpressionSyntax tuple)
        {
            AddTupleAssignment(ref walker, tuple, assignment.Right);
            return;
        }

        var root = walker.AddLocation(assignment.Left);
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            walker.AddRead(root);
        }

        walker.AddValue(assignment.Right);
        walker.AddWrite(root);
        if (walker.Model.GetSymbol(assignment.Left) is IPropertySymbol property &&
            MemberEffects.IsComputedUserProperty(property) &&
            walker.MayWriteMemory(property.SetMethod))
        {
            walker.Writes.Memory = true;
        }
    }

    private static void AddTupleAssignment(
        ref EffectWalker walker,
        TupleExpressionSyntax tuple,
        ExpressionSyntax right
    )
    {
        foreach (var argument in tuple.Arguments)
        {
            walker.AddWrite(walker.AddLocation(argument.Expression));
        }

        walker.AddValue(right);
    }
}
