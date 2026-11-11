namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The effects of operators for <see cref="ExpressionEffects"/>: user-defined conversions, prefix and postfix
/// operators, increments and assignments.
/// </summary>
internal static class OperatorEffects
{
    // A user-defined conversion of the value (implicit, or the operator of a cast) is a call of user code
    public static void AddConversions(ref EffectWalker walker, ExpressionSyntax expression)
    {
        var model = walker.Model;
        if (model.GetSafeConversion(expression) is { IsUserDefined: true, MethodSymbol: { } conversion })
        {
            walker.AddUserCode(conversion);
        }

        if (expression is CastExpressionSyntax &&
            model.GetSymbol(expression) is IMethodSymbol { MethodKind: MethodKind.Conversion } castOperator)
        {
            walker.AddUserCode(castOperator);
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
        AddAccessorCalls(ref walker, operand, callsGetter: true);
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
        AddAccessorCalls(ref walker, assignment.Left,
            callsGetter: !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression));
    }

    // The accessors that assigning a computed property calls: the setter, after the getter when the old value is read
    // (a compound assignment, an increment). Each may read anything and writes what DeviceFunctionEffects says. An
    // object initializer and a with expression call the setter of each computed property they assign.
    public static void AddAccessorCalls(ref EffectWalker walker, ExpressionSyntax target, bool callsGetter)
    {
        if (walker.Model.GetSymbol(target) is not IPropertySymbol property ||
            !MemberEffects.IsUserAccessorProperty(property))
        {
            return;
        }

        walker.AddUserCode(callsGetter ? property.GetMethod : null, property.SetMethod);
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
