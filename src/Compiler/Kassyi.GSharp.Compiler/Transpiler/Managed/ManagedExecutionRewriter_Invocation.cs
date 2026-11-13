namespace Kassyi.GSharp.Compiler.Transpiler;

internal sealed partial class ManagedExecutionRewriter
{
    // __gsharp_resume(Foo(args), n[, target]) becomes Class.__gsharp_statemachine_Foo(args, __gsharp_ctx.__gsharp_call_frame_n[, ref target]),
    // or Class.__gsharp_enter_Foo(args, ref __gsharp_ctx.__gsharp_call_frame_n[, ref target]) for a frame held as an object
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

        var throughEntry = frameIndex.Token.Value is int index && index < _callFrames.Length
            ? _callFrames[index].ThroughEntry
            : throw new InvalidOperationException(
                $"A resume call names frame {frameIndex.Token.ValueText}, which the context does not hold.");
        callArguments = callArguments.Add(CreateFrameArgument(frameIndex.Token.ValueText, throughEntry));

        if (arguments.Count > 2)
        {
            var target = Visit(arguments[2].Expression) as ExpressionSyntax ?? arguments[2].Expression;
            callArguments = callArguments.Add(SyntaxFactory.Argument(target)
                .WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword)));
        }

        var prefix = throughEntry
            ? GSharpInternalNames.SuspendingCall.EnterPrefix
            : GSharpInternalNames.Kernel.StateMachinePrefix;
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.ParseExpression(ManagedStateMachineEmitter.GetDeviceFunctionMember(callee, prefix)),
            SyntaxFactory.ArgumentList(callArguments));
    }

    // The frame passed to the callee: __gsharp_ctx.__gsharp_call_frame_<i> (&__gsharp_ctx->... in an unsafe context), or
    // ref __gsharp_ctx.__gsharp_call_frame_<i> (ref __gsharp_ctx->...) to the entry of a frame held as an object
    private ArgumentSyntax CreateFrameArgument(string frameIndex, bool throughEntry)
    {
        var frameAccess = SyntaxFactory.MemberAccessExpression(
            _isUnsafe ? SyntaxKind.PointerMemberAccessExpression : SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
            SyntaxFactory.IdentifierName(GSharpInternalNames.SuspendingCall.CallFramePrefix + frameIndex)
        );
        if (throughEntry)
        {
            return SyntaxFactory.Argument(frameAccess).WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword));
        }

        return SyntaxFactory.Argument(_isUnsafe
            ? SyntaxFactory.PrefixUnaryExpression(SyntaxKind.AddressOfExpression, frameAccess)
            : frameAccess);
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
