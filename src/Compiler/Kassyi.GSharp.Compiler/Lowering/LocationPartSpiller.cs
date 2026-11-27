namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Spills the parts that select a storage location (array or pointer receiver, indices, pointer of <c>*p</c> or
/// <c>p-&gt;F</c>) into temporaries before the statement or call, keeping the location itself in place
/// (<see cref="SpilledArguments"/>).
/// </summary>
internal static class LocationPartSpiller
{
    /// <summary>
    /// Evaluates the parts of <paramref name="location"/> (the argument at <paramref name="index"/> of
    /// <paramref name="arguments"/>, or the target of an assignment) into temporaries via <paramref name="spill"/> when
    /// <see cref="SpilledArguments"/> requires it, returning the location with those parts replaced.
    /// </summary>
    public static ExpressionSyntax SpillLocation(
        ExpressionSyntax location,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        int index,
        SemanticModel? model,
        Func<ExpressionSyntax, ExpressionSyntax> spill)
    {
        switch (location)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return parenthesized.WithExpression(
                    SpillLocation(parenthesized.Expression, arguments, index, model, spill));
            case MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } member:
                return member.WithExpression(SpillLocation(member.Expression, arguments, index, model, spill));
            case MemberAccessExpressionSyntax
            {
                RawKind: (int)SyntaxKind.PointerMemberAccessExpression
            } pointerMember:
                return pointerMember.WithExpression(
                    SpillPointer(pointerMember.Expression, arguments, index, model, spill));
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PointerIndirectionExpression } indirection:
                return indirection.WithOperand(SpillPointer(indirection.Operand, arguments, index, model, spill));
            case ElementAccessExpressionSyntax elementAccess:
                var receiver = SpillReceiver(elementAccess.Expression, arguments, index, model, spill);
                var indices = elementAccess.ArgumentList.Arguments;
                var spilledIndices = ImmutableArray.CreateBuilder<ArgumentSyntax>(indices.Count);
                foreach (var elementIndex in indices)
                {
                    spilledIndices.Add(
                        SpilledArguments.CanStayInPlace(elementIndex.Expression, arguments, index, model)
                        ? elementIndex
                        : elementIndex.WithExpression(spill(elementIndex.Expression)));
                }

                return elementAccess
                    .WithExpression(receiver)
                    .WithArgumentList(elementAccess.ArgumentList.WithArguments(
                        SyntaxFactory.SeparatedList(spilledIndices, indices.GetSeparators())));
            default:
                return location;
        }
    }

    // The receiver of an element access in a location: an array or pointer that cannot stay in place is evaluated
    // once (SpilledArguments.IsSpilledReceiver); any other receiver is a location of its own
    private static ExpressionSyntax SpillReceiver(
        ExpressionSyntax receiver,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        int index,
        SemanticModel? model,
        Func<ExpressionSyntax, ExpressionSyntax> spill)
    {
        if (SpilledArguments.IsSpilledReceiver(receiver, arguments, index, model))
        {
            return spill(receiver);
        }

        return model != null && SpilledArguments.IsValueReceiver(receiver, model)
            ? receiver
            : SpillLocation(receiver, arguments, index, model, spill);
    }

    // The pointer of *p or p->F: a value, evaluated once unless it can stay in place
    private static ExpressionSyntax SpillPointer(
        ExpressionSyntax pointer,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        int index,
        SemanticModel? model,
        Func<ExpressionSyntax, ExpressionSyntax> spill) =>
        SpilledArguments.CanStayInPlace(pointer, arguments, index, model)
            ? pointer
            : spill(pointer);
}
