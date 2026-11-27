namespace Kassyi.GSharp.Compiler.Transpiler;

internal sealed partial class ManagedExecutionRewriter
{
    // A block barrier or a warp collective of a stack form, a call of the user's code (qualified, routed, with its
    // caller information and, in a stack form, the frame of a device function that reaches a warp collective) or a
    // resume call of the lowered state machine (RewriteResumeCall)
    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // The test of a lowered user-defined && or ||: <accessor of operator false or true>([in] <left>)
        if (node.Expression is IdentifierNameSyntax { Identifier.ValueText: GSharpInternalNames.Lowering.TruthTest })
        {
            var test = OperatorMacroSyntax.GetTruthTest(node, semanticModel)
                       ?? throw new InvalidOperationException(
                           "G#: internal error: a truth test without its operator (please report this G# bug).");
            var operand = (Visit(node.ArgumentList.Arguments[0].Expression) as ExpressionSyntax
                           ?? node.ArgumentList.Arguments[0].Expression).WithoutTrivia();
            return ManagedOperatorMacroExpander.CreateTruthTestCall(test, operand, accessors)
                .WithTriviaFrom(node);
        }

        if (TryRewriteStackBarrier(node) is { } stackBarrier)
        {
            return stackBarrier;
        }

        if (TryRewriteStackWarp(node) is { } stackWarp)
        {
            return stackWarp;
        }

        if (node.Expression is not IdentifierNameSyntax
            {
                Identifier.ValueText: GSharpInternalNames.SuspendingCall.ResumeCall
            })
        {
            var result = AddWarpFrameArgument(node, AddCallerInfoArguments(node,
                ManagedNameQualifier.TryRewriteInvocation(node, this, semanticModel) is { } rewritten
                    ? RouteExtensionCall(node, rewritten)
                    : base.VisitInvocationExpression(node)));
            return ManagedRewriteClassifier.IsSequentialSum(node, semanticModel) &&
                   result is InvocationExpressionSyntax sumCall
                ? RewriteSequentialSum(sumCall)
                : result;
        }

        return RewriteResumeCall(node);
    }

    // In a stack form, a warp collective becomes the one call of the managed stack model that publishes, waits for the
    // lanes of the mask and reads, with the frame, the site and the place first and the user's arguments after them
    // (named ones keep their names: the parameters have the names of the API):
    // global::Kassyi.GSharp.Core.ManagedGpuContext.__gsharp_stack_warp_<kind>[<T>](__gsharp_warp_frame, <site>,
    //     "<place>", <arguments>); null for any other call
    private InvocationExpressionSyntax? TryRewriteStackWarp(InvocationExpressionSyntax node)
    {
        if (bodyKind != ManagedBodyKind.StackBody ||
            semanticModel.GetSymbol(node) is not IMethodSymbol method ||
            SyncIntrinsicClassifier.Classify(method) is not { Kind: SyncIntrinsicKind.WarpShuffle } intrinsic)
        {
            return null;
        }

        var layout = WarpLayout ?? throw new InvalidOperationException(
            $"G#: internal error: a stack form without a warp layout reaches {method.Name} " +
            "(please report this G# bug).");
        var shape = WarpShuffleShape.ForKind(intrinsic.Shuffle);
        var site = layout.GetSite(node);
        var place = SourcePlace.Format(semanticModel, node);
        var visited = Visit(node.ArgumentList) as ArgumentListSyntax ?? node.ArgumentList;
        var arguments = visited.Arguments
            .Insert(0, SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GSharpInternalNames.StackWarp.Frame)))
            .Insert(1, SyntaxFactory.Argument(
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(site))))
            .Insert(2, SyntaxFactory.Argument(
                SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(place))));
        return SyntaxFactory.InvocationExpression(
            ManagedNames.CreateMemberAccess(CreateStackWarpName(shape.StackFunction, method)),
            visited.WithArguments(arguments)
        ).WithTriviaFrom(node);
    }

    // The entry of a collective whose value has the type of the API overload (a shuffle, a reduction, a match) takes it
    // as its type argument, so that the value is converted as the user's call converts it (a short passed to the int
    // overload stays an int): __gsharp_stack_warp_<kind><T>; a vote or Gpu.SyncWarp: __gsharp_stack_warp_<kind>
    private static SimpleNameSyntax CreateStackWarpName(string stackFunction, IMethodSymbol method)
    {
        if (method.Parameters.Length < 2 ||
            method.Parameters[WarpShuffleShape.ShuffleValuePosition].Type.SpecialType == SpecialType.System_Boolean)
        {
            return SyntaxFactory.IdentifierName(stackFunction);
        }

        using var writer = new SourceWriter();
        writer.AppendCsharpTypeName(method.Parameters[WarpShuffleShape.ShuffleValuePosition].Type);
        return SyntaxFactory.GenericName(
            SyntaxFactory.Identifier(stackFunction),
            SyntaxFactory.TypeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.ParseTypeName(writer.ToString())))
        );
    }

    // In a stack form, the call of a device function that reaches a warp collective passes the offset of its frame
    // first (WarpFrameLayout): <call>(__gsharp_warp_frame + <offset>, <arguments>)
    private SyntaxNode? AddWarpFrameArgument(InvocationExpressionSyntax node, SyntaxNode? visited)
    {
        if (WarpLayout is not { } layout ||
            visited is not InvocationExpressionSyntax call ||
            semanticModel.GetSymbol(node) is not IMethodSymbol callee ||
            SyncIntrinsicClassifier.Classify(callee).Kind == SyncIntrinsicKind.WarpShuffle ||
            !SuspensionAnalysis.ReachesWarpShuffleOnStack(callee, semanticModel.Compilation))
        {
            return visited;
        }

        // __gsharp_warp_frame + <offset>
        var frame = SyntaxFactory.BinaryExpression(
            SyntaxKind.AddExpression,
            SyntaxFactory.IdentifierName(GSharpInternalNames.StackWarp.Frame),
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(layout.GetCallOffset(node)))
        );
        return call.WithArgumentList(
            call.ArgumentList.WithArguments(call.ArgumentList.Arguments.Insert(0, SyntaxFactory.Argument(frame))));
    }

    // __gsharp_resume(Foo(args), n[, target]) becomes Class.__gsharp_statemachine_Foo(args,
    // __gsharp_ctx.__gsharp_call_frame_n[, ref target]), or Class.__gsharp_enter_Foo(args, ref
    // __gsharp_ctx.__gsharp_call_frame_n[, ref target]) for a frame held as an object
    private InvocationExpressionSyntax RewriteResumeCall(InvocationExpressionSyntax node)
    {
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
                    SyntaxFactory.Argument(
                        ArgumentMatchingHelper.CreateOmittedArgument(callee.Parameters[i], call, semanticModel)));
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

    // In a stack form, a block barrier or grid barrier becomes the barrier of the managed stack model:
    // global::Kassyi.GSharp.Core.ManagedGpuContext.__gsharp_stack_block_barrier() or
    // global::Kassyi.GSharp.Core.ManagedGpuContext.__gsharp_stack_grid_barrier(); null for any other call
    private InvocationExpressionSyntax? TryRewriteStackBarrier(InvocationExpressionSyntax node)
    {
        if (bodyKind != ManagedBodyKind.StackBody)
        {
            return null;
        }

        string? barrierName = null;
        if (BarrierHelper.IsGridBarrier(node))
        {
            barrierName = GSharpInternalNames.StackModel.GridBarrier;
        }
        else if (semanticModel.GetSymbol(node) is IMethodSymbol method)
        {
            barrierName = SyncIntrinsicClassifier.Classify(method).Kind switch
            {
                SyncIntrinsicKind.BlockBarrier => GSharpInternalNames.StackModel.BlockBarrier,
                SyncIntrinsicKind.GridBarrier when !GpuMacroClassifier.Classify(method).HasBody =>
                    GSharpInternalNames.StackModel.GridBarrier,
                _ => null
            };
        }

        return barrierName is null
            ? null
            : SyntaxFactory.InvocationExpression(
                ManagedNames.CreateMemberAccess(barrierName)
            ).WithTriviaFrom(node);
    }

    // The frame passed to the callee: __gsharp_ctx.__gsharp_call_frame_<i> (&__gsharp_ctx->... in an unsafe context),
    // or ref __gsharp_ctx.__gsharp_call_frame_<i> (ref __gsharp_ctx->...) to the entry of a frame held as an object
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

    // <call>(<arguments>, <parameter>: <value>): a caller-info parameter that the call of a method of the user's code,
    // or of a device function of another assembly, omits receives the value that C# computes at the user's call site
    // (#946). Left to C#, the generated file would be the site, and a managed version does not keep the attribute.
    // Calls of other methods (the runtime's, which the rewriter may map to other members) and SharedMemory<T>, whose
    // site key replaces its caller information, are left as they are
    private SyntaxNode? AddCallerInfoArguments(SyntaxNode node, SyntaxNode? visited)
    {
        if (visited is not (InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax) ||
            semanticModel.GetSymbol(node) is not IMethodSymbol method ||
            !ManagedCallForms.AddsCallerInfo(method))
        {
            return visited;
        }

        var list = visited switch
        {
            InvocationExpressionSyntax call => call.ArgumentList,
            BaseObjectCreationExpressionSyntax creation => creation.ArgumentList,
            _ => null
        };
        var added = false;
        foreach (var parameter in method.Parameters)
        {
            if (CallerInfoArgument.GetValue(parameter, node, semanticModel) is not { } value)
            {
                continue;
            }

            // <parameter name>: <value>
            list = (list ?? SyntaxFactory.ArgumentList()).AddArguments(SyntaxFactory.Argument(
                SyntaxFactory.NameColon(parameter.Name.ToCSharpIdentifier()),
                default,
                CallerInfoArgument.CreateLiteral(value)
            ));
            added = true;
        }

        return (added, visited, list) switch
        {
            (true, InvocationExpressionSyntax call, { } arguments) => call.WithArgumentList(arguments),
            (true, BaseObjectCreationExpressionSyntax creation, { } arguments) => creation.WithArgumentList(arguments),
            _ => visited
        };
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
