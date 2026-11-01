namespace Kassyi.GSharp.Compiler.Transpiler;

internal sealed partial class ManagedExecutionRewriter
{
    // __gsharp_resume(Foo(args), n[, target]) becomes Class.__gsharp_statemachine_Foo(args, __gsharp_ctx.__gsharp_call_frame_n[, ref target])
    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is not IdentifierNameSyntax
            {
                Identifier.ValueText: GSharpInternalNames.SuspendingCall.ResumeCall
            })
        {
            var result = ManagedNameQualifier.TryRewriteInvocation(node, this, semanticModel) is { } rewritten
                ? RouteExtensionCall(node, rewritten)
                : base.VisitInvocationExpression(node);
            return ManagedRewriteClassifier.IsSequentialSum(node, semanticModel) &&
                   result is InvocationExpressionSyntax sumCall
                ? RewriteSequentialSum(sumCall)
                : result;
        }

        var arguments = node.ArgumentList.Arguments;
        if (arguments[0].Expression is not InvocationExpressionSyntax call ||
            arguments[1].Expression is not LiteralExpressionSyntax frameIndex ||
            semanticModel.GetSymbol(call) is not IMethodSymbol callee)
        {
            throw new InvalidOperationException("A lowered resume call does not wrap a call to a device function.");
        }

        var callArguments = SyntaxFactory.SeparatedList<ArgumentSyntax>();
        var argumentsByParameter =
            ArgumentMatchingHelper.MatchArgumentsToParameters(call.ArgumentList.Arguments, callee);
        for (var i = 0; i < argumentsByParameter.Length; i++)
        {
            if (argumentsByParameter[i] is not { } argument)
            {
                callArguments = callArguments.Add(
                    SyntaxFactory.Argument(ArgumentMatchingHelper.CreateDefaultArgument(callee.Parameters[i])));
                continue;
            }

            // The frame and the result follow the user's arguments, so every argument is passed by position
            var visited = (Visit(argument) as ArgumentSyntax ?? argument).WithNameColon(null);
            callArguments = callArguments.Add(visited.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)
                ? visited.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword))
                : visited);
        }

        // __gsharp_ctx.__gsharp_call_frame_<frame index>
        var frameName = GSharpInternalNames.SuspendingCall.CallFramePrefix + frameIndex.Token.ValueText;
        var frame = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
            SyntaxFactory.IdentifierName(frameName)
        );
        callArguments = callArguments.Add(SyntaxFactory.Argument(frame));

        if (arguments.Count <= 2)
        {
            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression(ManagedStateMachineEmitter.GetDeviceFunctionMember(callee,
                    GSharpInternalNames.Kernel.StateMachinePrefix)),
                SyntaxFactory.ArgumentList(callArguments));
        }

        var target = Visit(arguments[2].Expression) as ExpressionSyntax ?? arguments[2].Expression;
        callArguments = callArguments.Add(SyntaxFactory.Argument(target)
            .WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword)));

        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.ParseExpression(ManagedStateMachineEmitter.GetDeviceFunctionMember(callee,
                GSharpInternalNames.Kernel.StateMachinePrefix)),
            SyntaxFactory.ArgumentList(callArguments));
    }

    private static InvocationExpressionSyntax RewriteSequentialSum(InvocationExpressionSyntax call) =>
        call.Expression switch
        {
            QualifiedNameSyntax qualified =>
                call.WithExpression(qualified.WithRight(WithSequentialSumName(qualified.Right))),
            MemberAccessExpressionSyntax access =>
                call.WithExpression(access.WithName(WithSequentialSumName(access.Name))),
            SimpleNameSyntax simple =>
                call.WithExpression(WithSequentialSumName(simple)),
            _ => call
        };

    private static SimpleNameSyntax WithSequentialSumName(SimpleNameSyntax name)
    {
        var identifier = SyntaxFactory.Identifier("SumSequential").WithTriviaFrom(name.Identifier);
        return name is GenericNameSyntax generic
            ? generic.WithIdentifier(identifier)
            : SyntaxFactory.IdentifierName(identifier).WithTriviaFrom(name);
    }
}
