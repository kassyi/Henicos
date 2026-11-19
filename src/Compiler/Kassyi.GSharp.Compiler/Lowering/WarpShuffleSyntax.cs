namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The two halves of a warp collective in the CPU state machine (<see cref="StateMachineLowerer"/>): the publish of the
/// lane's value before the state ends, and the read of the values of that instance in the next state.
/// </summary>
internal static class WarpShuffleSyntax
{
    // __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, <site>, <mask or 0>, <val or 0>, "<name>", "<place>");
    // Warp.ActiveMask() publishes the mask 0: it waits for no lane, and goes on in the next round
    internal static ExpressionStatementSyntax CreatePublish(InvocationExpressionSyntax shuffle,
        WarpShuffleShape shape, int site, string place)
    {
        var value = shape.HasValue
            ? GetShuffleArgument(shuffle, WarpShuffleShape.ShuffleValuePosition)
            : SyntaxFactory.Argument(IntegerLiteral(0));
        var mask = shape.HasMask
            ? GetMaskArgument(shuffle)
            : SyntaxFactory.Argument(IntegerLiteral(0));
        var arguments = SyntaxFactory.SeparatedList<ArgumentSyntax>()
            .Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GSharpInternalNames.SuspendingCall.Root)))
            .Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context)))
            .Add(SyntaxFactory.Argument(IntegerLiteral(site)))
            .Add(mask)
            .Add(value)
            .Add(SyntaxFactory.Argument(StringLiteral(shape.DisplayName)))
            .Add(SyntaxFactory.Argument(StringLiteral(place)));
        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(GSharpInternalNames.Prelude.WarpPublish),
                SyntaxFactory.ArgumentList(arguments)
            )
        );
    }

    private static LiteralExpressionSyntax IntegerLiteral(int value) =>
        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(value));

    private static LiteralExpressionSyntax StringLiteral(string value) =>
        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value));

    // The mask of the call; Gpu.SyncWarp() without one names every lane, as its default value does
    private static ArgumentSyntax GetMaskArgument(InvocationExpressionSyntax shuffle) =>
        TryGetShuffleArgument(shuffle, WarpShuffleShape.ShuffleMaskPosition, out var mask)
            ? mask
            : SyntaxFactory.Argument(
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                    SyntaxFactory.Literal("0xFFFFFFFFu", 0xFFFFFFFFu))
            );

    // stmt with the shuffle replaced by the read of the values that the lanes published for this instance of the call
    internal static StatementSyntax RewriteRead(StatementSyntax stmt, InvocationExpressionSyntax barrierInv,
        WarpShuffleShape shape, int site, string place)
    {
        // The root context owns the published values of the lane, also when the call is in a device function; the frame
        // gives the path and the visit count of this instance
        var newArgs = SyntaxFactory.SeparatedList<ArgumentSyntax>()
            .Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GSharpInternalNames.SuspendingCall.Root)))
            .Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context)))
            .Add(SyntaxFactory.Argument(IntegerLiteral(site)));
        if (shape.HasMask)
        {
            newArgs = newArgs.Add(GetMaskArgument(barrierInv));
        }

        if (shape.HasValue)
        {
            // Pass val for type inference
            newArgs = newArgs.Add(GetShuffleArgument(barrierInv, WarpShuffleShape.ShuffleValuePosition));
        }

        if (shape.HasLane || shape.PredicateArgument is not null)
        {
            // For shuffles, this is srcLane/delta/mask; for MatchAll, this is pred (the out predicate argument)
            newArgs = newArgs.Add(GetShuffleArgument(barrierInv, WarpShuffleShape.ShuffleLanePosition));
        }

        if (shape.HasWidth)
        {
            // The width is always passed, so that the place of the shuffle can follow it; 32 is the default of every
            // shuffle
            newArgs = newArgs.Add(
                TryGetShuffleArgument(barrierInv, WarpShuffleShape.ShuffleWidthPosition, out var width)
                    ? width
                    : SyntaxFactory.Argument(IntegerLiteral(32))
            );
        }

        newArgs = newArgs.Add(SyntaxFactory.Argument(StringLiteral(place)));

        // __gsharp_warp_shuffle_read_<kind>(__gsharp_root, __gsharp_ctx, <site>, mask, val, delta, width,
        //     "<file>(<line>,<column>)")
        var rewrittenInv = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(shape.ReadFunction),
            SyntaxFactory.ArgumentList(newArgs)
        );

        return stmt.ReplaceNode(barrierInv, rewrittenInv);
    }

    // The argument of a shuffle for the parameter at position, which the call compiled with (so it is there)
    private static ArgumentSyntax GetShuffleArgument(InvocationExpressionSyntax shuffle, int position) =>
        TryGetShuffleArgument(shuffle, position, out var argument)
            ? argument
            : throw new InvalidOperationException($"A warp shuffle without the argument at position {position}");

    // The argument of a shuffle for the parameter at position, without its name: a named argument may stand anywhere
    // (Warp.ShuffleSync(m, v, width: 8, srcLane: 3)), and the read functions take the arguments by position
    private static bool TryGetShuffleArgument(
        InvocationExpressionSyntax shuffle,
        int position,
        [NotNullWhen(true)] out ArgumentSyntax? argument)
    {
        var arguments = shuffle.ArgumentList.Arguments;
        for (var i = 0; i < arguments.Count; i++)
        {
            var target = arguments[i].NameColon?.Name.Identifier.ValueText switch
            {
                null => i,
                WarpShuffleShape.ShuffleMaskParameter => WarpShuffleShape.ShuffleMaskPosition,
                WarpShuffleShape.ShuffleValueParameter or WarpShuffleShape.MatchValueParameter or
                    WarpShuffleShape.VotePredicateParameter or WarpShuffleShape.ReduceValueParameter =>
                    WarpShuffleShape.ShuffleValuePosition,
                WarpShuffleShape.ShuffleWidthParameter => WarpShuffleShape.ShuffleWidthPosition,
                _ => WarpShuffleShape.ShuffleLanePosition
            };
            if (target == position)
            {
                argument = arguments[i].WithNameColon(null);
                return true;
            }
        }

        argument = null;
        return false;
    }
}
