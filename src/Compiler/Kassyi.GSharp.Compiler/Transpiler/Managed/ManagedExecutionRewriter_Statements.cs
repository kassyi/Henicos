using Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

internal sealed partial class ManagedExecutionRewriter
{
    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (TryRewriteSpecialStatement(node) is { } special)
        {
            return special;
        }

        // c = a + b; with an operator macro becomes the call of its target (#711)
        if (node.Expression is AssignmentExpressionSyntax assignment &&
            ManagedRewriteClassifier.TryGetOperatorMacro(assignment, semanticModel, out var macro) &&
            ManagedOperatorMacroExpander.Expand(assignment, macro, semanticModel, this, SpellDeviceFunction,
                accessors) is { } expanded)
        {
            return expanded.WithTriviaFrom(node);
        }

        if (node.Expression is not InvocationExpressionSyntax invocation ||
            !TryClassifyRewrittenMacro(invocation, out var call))
        {
            return base.VisitExpressionStatement(node);
        }

        // Read on the call in its tree: the copy that binds to the source has no ancestors
        var namesInScope = ManagedShadowedLocals.GetNamesInScope(invocation, semanticModel);
        invocation = BindCopiesToSource(invocation);
        StatementSyntax? rewritten = call.Kind switch
        {
            GpuMacroKind.ForUnroll => RewriteForUnroll(invocation, call, namesInScope),
            GpuMacroKind.GridStrideLoop => RewriteGridStrideLoop(invocation, call, namesInScope),
            _ => RewriteSingleThread(invocation, namesInScope)
        };
        return rewritten?.WithTriviaFrom(node) ?? base.VisitExpressionStatement(node);
    }

    // The statements that become something else entirely: a call that C# removes, which the generated file would
    // decide again with none of the #undef directives of the user's file (#1540), and an inline PTX call, which the CPU
    // cannot run
    private SyntaxNode? TryRewriteSpecialStatement(ExpressionStatementSyntax node)
    {
        if (RemovedCalls.IsRemovedCall(node.Expression, semanticModel))
        {
            return RemovedCallEraser.ToEmptyStatement(node);
        }

        if (node.Expression is InvocationExpressionSyntax inlinePtx &&
            DialectCallSupport.IsUnsupportedInlinePtxCall(inlinePtx, semanticModel, HostDialects.Cpu))
        {
            var message = DialectCallSupport.GetInlinePtxUnsupportedMessage(HostDialects.Cpu.Name);
            return SyntaxFactory.ParseStatement($"throw new global::System.NotSupportedException(\"{message}\");")
                .WithTriviaFrom(node);
        }

        return null;
    }

    // A for loop without the initializers and incrementors that are calls C# removes (#1540)
    public override SyntaxNode? VisitForStatement(ForStatementSyntax node) =>
        base.VisitForStatement(node) is ForStatementSyntax visited
            ? RemovedCallEraser.WithoutRemovedCalls(node, visited, semanticModel)
            : null;

    // The macro as the semantic model resolves it. A call that does not resolve is left alone, as in
    // PreLoweringExpander: the call is then not guessed from its name, and the unrewritten macro fails where it runs
    private bool TryClassifyRewrittenMacro(InvocationExpressionSyntax invocation, out GpuMacroCall call)
    {
        call = semanticModel.GetSymbol(invocation) is IMethodSymbol method
            ? GpuMacroClassifier.Classify(method)
            : default;
        return call.Kind is GpuMacroKind.ForUnroll or GpuMacroKind.GridStrideLoop or GpuMacroKind.SingleThread;
    }

    // [WHY] The rewrites below visit copies of the macro's parts: the lambda body with the loop parameter replaced
    // (MacroLambdaSubstituter), an expression body put into a block, arguments without trivia. A copy of a node of the
    // user's tree (the body of a managed version is not lowered) is outside that tree and binds to nothing, so no name in
    // it would be qualified or routed (#1151). With the original-span annotation on every node, as a lowered body has,
    // each copy binds through the node it was copied from. A call of a lowered body already carries its annotations
    private InvocationExpressionSyntax BindCopiesToSource(InvocationExpressionSyntax invocation) =>
        invocation.SyntaxTree == semanticModel.SyntaxTree
            ? LoweringAnnotations.AnnotateOriginalSpans(invocation, semanticModel.Compilation)
            : invocation;

    // Gpu.SingleThread(body) becomes:
    // if (<six indices are 0>) <body>
    // and, when the body has a return (not one of a lambda or local function of the body), which leaves the body as in
    // C# (#693):
    // { if (<six indices are 0>) <body with each return as goto __gsharp_st_end_<n>;> __gsharp_st_end_<n>: ; }
    // A body that is not a lambda (a method group) stays a call of the implementation in Kassyi.GSharp.Core, which
    // checks the same condition
    private StatementSyntax? RewriteSingleThread(InvocationExpressionSyntax invocation, HashSet<string> namesInScope)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1 || arguments[0].Expression is not LambdaExpressionSyntax lambda)
        {
            return null;
        }

        string? endLabel = SyncGridBody.ContainsBodyReturn(lambda.Body)
            ? $"{GSharpInternalNames.Lowering.SingleThreadEndLabelPrefix}{_nextSingleThreadId++}"
            : null;
        var outerLabel = _singleThreadEndLabel;
        var outerDepth = _singleThreadDepth;
        _singleThreadEndLabel = endLabel;
        // VisitLoopBody visits the body one lambda level deeper
        _singleThreadDepth = _inLambdaOrLocalFuncCount + 1;
        BlockSyntax body;
        try
        {
            body = VisitLoopBody(lambda, substituter: null, namesInScope);
        }
        finally
        {
            _singleThreadEndLabel = outerLabel;
            _singleThreadDepth = outerDepth;
        }

        var guarded = SyntaxFactory.IfStatement(SingleThreadGuard.CreateManagedCondition(), body);
        if (endLabel is null)
        {
            return guarded;
        }

        // { <guarded> <label>: ; }
        return SyntaxFactory.Block(guarded, LoweringSyntax.EmptyLabel(endLabel));
    }

    // Gpu.GridStrideLoop([ref ctx,] count, body) becomes:
    // {
    //     uint __gsharp_gsl_n_<n> = ManagedGpuContext.GridStrideCount(<count>);
    //     uint __gsharp_gsl_s_<n> = ManagedGpuContext.GridStrideStep();
    //     for (uint __gsharp_gsl_<n> = ManagedGpuContext.GridStrideStart(); __gsharp_gsl_<n> < __gsharp_gsl_n_<n>;
    //          __gsharp_gsl_<n> += __gsharp_gsl_s_<n>)
    //     {
    //         int __gsharp_unroll_<n> = (int)__gsharp_gsl_<n>;
    //         <body>
    //     }
    // }
    // The same loop as the C++ one (GridStrideLoopMacroRule) and the Core implementation, through the same helpers: the
    // count and the step are read once, and the index is unsigned with a start and step that saturate at 2^31 (#712).
    // The loop variable has a name of its own, like the one of ForUnroll: the lambda parameter may be named like a local
    // of the kernel, which the loop would otherwise hide (CS0136)
    private BlockSyntax? RewriteGridStrideLoop(InvocationExpressionSyntax invocation, GpuMacroCall call,
        HashSet<string> namesInScope)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (!TryGetLoopMacroBody(arguments, call, out var lambda, out var loopParameter) ||
            call.GetArgument(arguments, call.ValueOrdinal)?.Expression is not { } countArg)
        {
            return null;
        }

        var number = _nextUnrollId;
        var names = new GridStrideNames(
            NextLoopVariableName(),
            $"{GSharpInternalNames.Lowering.GridStrideIndexPrefix}{number}",
            $"{GSharpInternalNames.Lowering.GridStrideCountPrefix}{number}",
            $"{GSharpInternalNames.Lowering.GridStrideStepPrefix}{number}"
        );
        // Visit the count so that context accesses in it are rewritten like the rest of the body
        var countExpr = Visit(countArg) as ExpressionSyntax ?? countArg;
        var (countDeclaration, stepDeclaration) = DeclareGridStrideBounds(names, countExpr);

        var substituter = CreateSubstituter(call.GetContext(arguments), lambda, loopParameter, names.Index);
        var body = VisitLoopBody(lambda, substituter, namesInScope);
        var loop = CreateGridStrideFor(names, body.Statements.Insert(0, DeclareGridStrideIndex(names)));

        return SyntaxFactory.Block(countDeclaration, stepDeclaration, loop);
    }

    // The names of a grid-stride loop: the int index the body reads, the unsigned index the loop steps, the count and
    // the step
    private readonly record struct GridStrideNames(string Index, string UnsignedIndex, string Count, string Step);

    // uint <count> = ManagedGpuContext.GridStrideCount(<count expression>);
    // uint <step> = ManagedGpuContext.GridStrideStep();
    private static (StatementSyntax Count, StatementSyntax Step) DeclareGridStrideBounds(in GridStrideNames names,
        ExpressionSyntax countExpr)
    {
        var count = DeclareLocal(
            SyntaxKind.UIntKeyword,
            names.Count,
            SyntaxFactory.InvocationExpression(
                ManagedNames.CreateMemberAccess("GridStrideCount"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(countExpr)))
            )
        );
        var step = DeclareLocal(
            SyntaxKind.UIntKeyword,
            names.Step,
            SyntaxFactory.InvocationExpression(ManagedNames.CreateMemberAccess("GridStrideStep"))
        );
        return (count, step);
    }

    // int <index> = (int)<unsigned index>;
    private static StatementSyntax DeclareGridStrideIndex(in GridStrideNames names) =>
        DeclareLocal(
            SyntaxKind.IntKeyword,
            names.Index,
            SyntaxFactory.CastExpression(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                SyntaxFactory.IdentifierName(names.UnsignedIndex)
            )
        );

    // for (uint <unsigned index> = ManagedGpuContext.GridStrideStart(); <unsigned index> < <count>;
    //      <unsigned index> += <step>) { <statements> }
    private static ForStatementSyntax CreateGridStrideFor(in GridStrideNames names,
        SyntaxList<StatementSyntax> statements)
    {
        var index = SyntaxFactory.IdentifierName(names.UnsignedIndex);
        return SyntaxFactory.ForStatement(
            DeclareVariable(
                SyntaxKind.UIntKeyword,
                names.UnsignedIndex,
                SyntaxFactory.InvocationExpression(ManagedNames.CreateMemberAccess("GridStrideStart"))
            ),
            SyntaxFactory.SeparatedList<ExpressionSyntax>(),
            SyntaxFactory.BinaryExpression(
                SyntaxKind.LessThanExpression,
                index,
                SyntaxFactory.IdentifierName(names.Count)
            ),
            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.AddAssignmentExpression,
                    index,
                    SyntaxFactory.IdentifierName(names.Step)
                )
            ),
            SyntaxFactory.Block(statements)
        );
    }

    // <type> <name> = <value>
    private static VariableDeclarationSyntax DeclareVariable(SyntaxKind type, string name, ExpressionSyntax value) =>
        DeclareVariable(SyntaxFactory.PredefinedType(SyntaxFactory.Token(type)), name, value);

    // <type> <name> = <value>
    private static VariableDeclarationSyntax DeclareVariable(TypeSyntax type, string name, ExpressionSyntax value) =>
        SyntaxFactory.VariableDeclaration(
            type,
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name))
                    .WithInitializer(SyntaxFactory.EqualsValueClause(value))
            )
        );

    // The names of every local that G# declares start with it; a user name cannot (GSH0055)
    private const string InternalNamePrefix = "__gsharp_";

    // var __gsharp_<name> = <value>, a temporary that lowering declared, becomes <type> __gsharp_<name> = <value>: the
    // body is emitted into the user's namespace, where a type named var would capture var
    public override SyntaxNode? VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        var visited = base.VisitVariableDeclaration(node);
        if (visited is not VariableDeclarationSyntax declaration || !node.Type.IsVar || node.Variables.Count != 1 ||
            !node.Variables[0].Identifier.ValueText.StartsWith(InternalNamePrefix, StringComparison.Ordinal))
        {
            return visited;
        }

        return declaration.WithType(ResolveTemporaryType(node.Variables[0]).ToCsharpTypeSyntax()
            .WithTriviaFrom(declaration.Type));
    }

    // The type of a temporary: a numeric literal's own type, otherwise as a hoisted temporary's type is resolved
    private ITypeSymbol ResolveTemporaryType(VariableDeclaratorSyntax declarator) =>
        declarator.Initializer?.Value is LiteralExpressionSyntax { Token.Value: { } value } &&
        GetLiteralType(value) is { } literalType
            ? semanticModel.Compilation.GetSpecialType(literalType)
            : HoistedFieldTypeResolver.Resolve(declarator, semanticModel, declarator.Identifier.ValueText);

    private static SpecialType? GetLiteralType(object value) => value switch
    {
        int => SpecialType.System_Int32,
        uint => SpecialType.System_UInt32,
        long => SpecialType.System_Int64,
        ulong => SpecialType.System_UInt64,
        float => SpecialType.System_Single,
        double => SpecialType.System_Double,
        bool => SpecialType.System_Boolean,
        _ => null
    };

    // using (<variable>) <body> becomes using (<type> __gsharp_using_resource_<n> = <variable>) <body>: the hidden
    // variable of the C# specification written out, as the C++ output does (UsingTranspiler). C# disposes the copy
    // either way, and the declaration form keeps the copied body from raising CS0728 a second time, in the generated
    // file, for an assignment to the variable that the user's source already reports
    public override SyntaxNode? VisitUsingStatement(UsingStatementSyntax node)
    {
        var rewritten = base.VisitUsingStatement(node);
        if (rewritten is not UsingStatementSyntax visited ||
            visited is not { Declaration: null, Expression: IdentifierNameSyntax variable })
        {
            return rewritten;
        }

        var name = GSharpInternalNames.Using.ResourcePrefix + _nextUsingResourceId++;
        var type = semanticModel.GetTypeSymbol(node.Expression ?? variable) ??
            throw new InvalidOperationException($"The resource of 'using ({variable.Identifier.ValueText})' has no type.");
        var declaration = DeclareVariable(type.ToCsharpTypeSyntax(), name, variable.WithoutTrivia());
        return visited
            .WithExpression(null)
            .WithDeclaration(declaration);
    }

    // <type> <name> = <value>;
    private static LocalDeclarationStatementSyntax DeclareLocal(SyntaxKind type, string name, ExpressionSyntax value) =>
        SyntaxFactory.LocalDeclarationStatement(DeclareVariable(type, name, value));

    // Gpu.ForUnroll([ref ctx,] start, count, body) becomes:
    // for (int __gsharp_unroll_<n> = <start>; __gsharp_unroll_<n> < unchecked(<start> + <count>);
    //      __gsharp_unroll_<n>++) <body>
    // The loop variable has a name of its own, so that it never meets a local that the lambda parameter is named like.
    // The sum is unchecked, as in the C# method and in the C++ loop: a sum beyond int.MaxValue wraps around and the
    // loop runs no iteration, where a constant sum would not compile
    private ForStatementSyntax? RewriteForUnroll(InvocationExpressionSyntax invocation, GpuMacroCall call,
        HashSet<string> namesInScope)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (!TryGetLoopMacroBody(arguments, call, out var lambda, out var loopParameter) ||
            call.GetArgument(arguments, call.ValueOrdinal)?.Expression is not { } start ||
            call.GetArgument(arguments, call.ValueOrdinal + 1)?.Expression is not { } count)
        {
            return null;
        }

        var startArg = start.WithoutTrivia();
        var countArg = count.WithoutTrivia();
        var startExpr = Visit(startArg) as ExpressionSyntax ?? startArg;
        var countExpr = Visit(countArg) as ExpressionSyntax ?? countArg;
        var indexName = NextLoopVariableName();

        var substituter = CreateSubstituter(call.GetContext(arguments), lambda, loopParameter, indexName);
        var body = VisitLoopBody(lambda, substituter, namesInScope);

        var declaration = DeclareVariable(SyntaxKind.IntKeyword, indexName, startExpr);

        // <index> < unchecked(<start> + <count>)
        var condition = SyntaxFactory.BinaryExpression(
            SyntaxKind.LessThanExpression,
            SyntaxFactory.IdentifierName(indexName),
            SyntaxFactory.CheckedExpression(
                SyntaxKind.UncheckedExpression,
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.AddExpression,
                    Parenthesize(startArg, startExpr),
                    Parenthesize(countArg, countExpr)
                )
            )
        );

        // <index>++
        var incrementor = SyntaxFactory.PostfixUnaryExpression(
            SyntaxKind.PostIncrementExpression,
            SyntaxFactory.IdentifierName(indexName)
        );

        return SyntaxFactory.ForStatement(
            declaration,
            SyntaxFactory.SeparatedList<ExpressionSyntax>(),
            condition,
            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(incrementor),
            body
        );
    }

    // The numbers continue from the loops that PreLoweringExpander made, so that every loop variable of the kernel
    // differs
    private string NextLoopVariableName() => $"{GSharpInternalNames.Lowering.UnrollIndexPrefix}{_nextUnrollId++}";

    private static bool TryGetLoopMacroBody(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        GpuMacroCall call,
        [NotNullWhen(true)] out LambdaExpressionSyntax? lambda,
        [NotNullWhen(true)] out ParameterSyntax? loopParameter)
    {
        loopParameter = null;
        lambda = null;
        if (call.GetBody(arguments) is not { } bodyLambda ||
            GpuCodeConstraints.GetMacroLoopParameter(bodyLambda, call.HasContext) is not { } parameter ||
            parameter.Identifier.ValueText.Length == 0)
        {
            return false;
        }

        lambda = bodyLambda;
        loopParameter = parameter;
        return true;
    }

    // Replaces the loop parameter by <loopName> and, for the ref ctx overloads, the context parameter by the context
    // argument
    private MacroLambdaSubstituter CreateSubstituter(
        ExpressionSyntax? context,
        LambdaExpressionSyntax lambda,
        ParameterSyntax loopParameter,
        string loopName)
    {
        ParameterSyntax? contextParameter = null;
        ExpressionSyntax? contextArgument = null;
        if (lambda is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 2 } parenthesized &&
            context is not null)
        {
            contextParameter = parenthesized.ParameterList.Parameters[0];
            contextArgument = context.WithoutTrivia();
        }

        return new MacroLambdaSubstituter(semanticModel, loopParameter, loopName, contextParameter, contextArgument);
    }

    // The body of the macro lambda as a block, its locals that shadow a name of <namesInScope> renamed
    // (ManagedShadowedLocals). A return in it is not rewritten as the kernel's return
    private BlockSyntax VisitLoopBody(LambdaExpressionSyntax lambda, MacroLambdaSubstituter? substituter,
        HashSet<string> namesInScope)
    {
        var renamed = ManagedShadowedLocals.Rename(lambda.Body, namesInScope, semanticModel);
        var substituted = substituter is null ? renamed : substituter.Visit(renamed) ?? renamed;
        var block = substituted as BlockSyntax ??
                    SyntaxFactory.Block(SyntaxFactory.ExpressionStatement((ExpressionSyntax)substituted));

        _inLambdaOrLocalFuncCount++;
        try
        {
            return Visit(block) as BlockSyntax ?? block;
        }
        finally
        {
            _inLambdaOrLocalFuncCount--;
        }
    }

    // A compound operand keeps its grouping inside the sum. The operand as written decides: a name that the rewrite
    // qualified (ManagedNameQualifier) is still a single operand
    private static ExpressionSyntax Parenthesize(ExpressionSyntax written, ExpressionSyntax rewritten) =>
        written is LiteralExpressionSyntax or IdentifierNameSyntax or ParenthesizedExpressionSyntax
            ? rewritten
            : SyntaxFactory.ParenthesizedExpression(rewritten);

    public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
    {
        // A return of a Gpu.SingleThread body leaves the body: goto <label>;
        if (_singleThreadEndLabel is { } singleThreadEnd && _inLambdaOrLocalFuncCount == _singleThreadDepth)
        {
            return LoweringSyntax.Goto(singleThreadEnd).WithTriviaFrom(node);
        }

        // A managed version returns as the user's method does
        if (bodyKind == ManagedBodyKind.ManagedVersion)
        {
            return base.VisitReturnStatement(node);
        }

        if (bodyKind == ManagedBodyKind.SuspendingDeviceFunction && _inLambdaOrLocalFuncCount == 0)
        {
            return RewriteDeviceFunctionReturn(node);
        }

        if (_inLambdaOrLocalFuncCount != 0 ||
            node.HasAnnotations(LoweringAnnotations.StateMachineReturnAnnotationKind))
        {
            return base.VisitReturnStatement(node);
        }

        var statements = ImmutableArray.CreateBuilder<StatementSyntax>(3);

        // { [__gsharp_auto_result = <value>;] __gsharp_ctx.__gsharp_state = -1; return; }
        if (node.Expression != null)
        {
            statements.Add(
                CreateAssignment(
                    SyntaxFactory.IdentifierName(GSharpInternalNames.ReturnValue.Buffer),
                    Visit(node.Expression) as ExpressionSyntax ?? node.Expression
                )
            );
        }

        statements.Add(CreateContextStateAssignment(-1));
        statements.Add(SyntaxFactory.ReturnStatement());

        return SyntaxFactory.Block(statements).WithTriviaFrom(node);
    }

    // A suspension reports "not completed"; a return of the user's code writes the result, resets the frame for the
    // next call and reports completion
    private SyntaxNode RewriteDeviceFunctionReturn(ReturnStatementSyntax node)
    {
        if (node.HasAnnotations(LoweringAnnotations.StateMachineReturnAnnotationKind))
        {
            // return false;
            return SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression))
                .WithTriviaFrom(node);
        }

        // { [__gsharp_result = <value>;] __gsharp_ctx.__gsharp_state = 0; return true; }
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>(3);
        if (node.Expression != null)
        {
            statements.Add(
                CreateAssignment(
                    SyntaxFactory.IdentifierName(GSharpInternalNames.SuspendingCall.Result),
                    Visit(node.Expression) as ExpressionSyntax ?? node.Expression
                )
            );
        }

        statements.Add(CreateContextStateAssignment(0));
        statements.Add(
            SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));

        return SyntaxFactory.Block(statements).WithTriviaFrom(node);
    }

    // <target> = <value>;
    private static ExpressionStatementSyntax CreateAssignment(ExpressionSyntax target, ExpressionSyntax value) =>
        SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, target, value)
        );

    // __gsharp_ctx->__gsharp_state = <state>;
    private ExpressionStatementSyntax CreateContextStateAssignment(int state) =>
        CreateAssignment(
            SyntaxFactory.MemberAccessExpression(
                _isUnsafe ? SyntaxKind.PointerMemberAccessExpression : SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
                SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.State)
            ),
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(state))
        );
}
