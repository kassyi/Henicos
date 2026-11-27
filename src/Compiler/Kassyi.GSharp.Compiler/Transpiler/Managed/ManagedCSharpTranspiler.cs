using Kassyi.GSharp.Compiler.Transpiler.Frames;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Transpiles a C# method declaration into a pure C# managed state machine and wrapper for host-side debugging.
/// </summary>
/// <remarks>
/// Types named from symbols go through <see cref="CSharpTypeNameWriter.AppendCsharpTypeName"/>; syntax nodes become
/// text only in <see cref="ManagedStateMachineEmitter.ToSource"/>.
/// </remarks>
internal static class ManagedCSharpTranspiler
{
    /// <summary>
    /// Emits the context class and the state machine of a kernel, or returns an empty string for a kernel
    /// without a body.
    /// </summary>
    public static string Generate(in MethodEmissionPlan plan)
    {
        // A kernel has a state machine exactly when it has a body, and then always a symbol
        if (plan.Lowered is not { } lowered || plan.Symbol is not { } methodSymbol)
        {
            return string.Empty;
        }

        var methodDecl = plan.Declaration;
        var semanticModel = plan.SemanticModel;
        var loweredBody = lowered.LoweredBody;
        var contextFields = lowered.ContextFields;
        var contextName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.ContextPrefix, methodSymbol);
        var stateMachineName =
            GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.StateMachinePrefix, methodSymbol);
        // Internal-error reports name the kernel as the user wrote it.
        var kernelName = methodDecl.Identifier.ValueText;

        var (localInits, regularFields, unhoistedLocals, renamedLocals, slots) =
            ManagedContextFields.ProcessContextFields(contextFields, semanticModel, kernelName, loweredBody);
        var copiedParameters = plan.GetCopiedParameters(lowered);
        var callees = GetCallees(lowered.CallSites, semanticModel);
        var frame = new StateMachineFrame(copiedParameters, slots, callees, lowered.WarpSiteCount);

        var isUnsafe = ManagedStateMachineAvailability.NeedsUnsafe(methodSymbol, methodDecl) ||
                       ManagedStateMachineEmitter.HasPointerField(frame);
        var compilation = semanticModel.Compilation;
        var accessors = new ManagedAccessorSet(methodSymbol, compilation);

        // A callee without a frame (a file-local type, GSH0119, or the diagnostics of the callee) leaves a body that
        // throws, and no call frames
        var callFrames = ManagedStateMachineAvailability.PlanCallFrames(callees, methodSymbol.ContainingType,
            compilation, out var unavailable) ?? ImmutableArray<ManagedCallFrame>.Empty;
        var bodySource = unavailable is null
            ? CreateBody(lowered, unhoistedLocals, renamedLocals, semanticModel, kernelName,
                methodSymbol.AreLocalsZeroed(),
                new ManagedExecutionRewriter(semanticModel, ManagedBodyKind.Kernel, accessors, isUnsafe, callFrames))
            : CreateUnavailableFrameThrow(kernelName, unavailable);

        var accessibility = KernelFrameAccessibility.Create(methodSymbol,
            GetFrameFieldTypes(frame),
            ManagedStateMachineAvailability.GetCallFrames(callFrames, compilation));
        using var writer = new SourceWriter();
        ManagedStateMachineEmitter.EmitContextClass(
            writer, contextName, frame, callFrames, accessibility.Context,
            Accessibility.Public, isUnsafe: isUnsafe);
        ManagedStateMachineEmitter.EmitStateMachineMethod(
            writer, contextName, stateMachineName, methodSymbol, plan.HasAutoResult, frame,
            bodySource, accessibility.StateMachine, isUnsafe: isUnsafe);
        accessors.WriteTo(writer);
        ManagedWarningRestore.Append(writer, ManagedWarningRestore.Create(plan.Declaration));
        return writer.ToString();
    }

    // The types of the context's fields: the copied parameters and the hoisted locals
    private static ImmutableArray<ITypeSymbol> GetFrameFieldTypes(StateMachineFrame frame)
    {
        var types = ImmutableArray.CreateBuilder<ITypeSymbol>();
        frame.CopiedParameters
            .AsStackEnumerable()
            .Select(static p => p.Type)
            .AddTo(types);
        frame.Slots
            .AsStackEnumerable()
            .Where(static s => s.Kind == FrameSlotKind.Field)
            .Select(static s => s.Type)
            .AddTo(types);
        return types.ToImmutable();
    }

    // The functions of the calls that may suspend, in call order
    private static ImmutableArray<IMethodSymbol> GetCallees(IReadOnlyList<InvocationExpressionSyntax> callSites,
        SemanticModel semanticModel)
    {
        var callees = ImmutableArray.CreateBuilder<IMethodSymbol>(callSites.Count);
        foreach (var callSite in callSites)
        {
            callees.Add(semanticModel.GetSymbol(callSite) as IMethodSymbol
                        ?? throw new InvalidOperationException(
                            "A call that may suspend does not resolve to a device function."));
        }

        return callees.MoveToImmutable();
    }

    // throw new global::System.NotSupportedException("<kernel> cannot run on the managed CPU path: ...");
    private static string CreateUnavailableFrameThrow(string methodName, IMethodSymbol callee) =>
        $"throw new global::System.NotSupportedException(\"{methodName} cannot run on the managed CPU path: the " +
        $"managed state machine of '{callee.ContainingType.Name}.{callee.Name}' is not generated " +
        $"({GSharpDiagnostics.FileLocalTypeInManagedCopy.Id}, or the diagnostics of that function).\");";

    // throw new global::System.NotSupportedException("<method> cannot run on the managed CPU path: ...");
    // naming the place of each goto, as the error sites of the other paths do (SourcePlace.Format)
    private static string CreateChainedLabelJumpThrow(string methodName, ImmutableArray<ChainedLabelGoto> jumps,
        SemanticModel semanticModel)
    {
        var places = ImmutableArray.CreateBuilder<string>(jumps.Length);
        jumps
            .AsStackEnumerable()
            .Select(semanticModel, static (jump, model) =>
                $"'goto {jump.Label}' at {SourcePlace.Format(model, jump.Goto)}")
            .AddTo(places);
        return $"throw new global::System.NotSupportedException(\"{methodName} cannot run on the managed CPU path: " +
               $"{string.Join(", ", places)} jumps to the inner label of chained labels from another state " +
               $"({GSharpDiagnostics.GotoToChainedLabelAcrossStates.Id}, or " +
               $"{GSharpDiagnostics.CrossBarrierGotoProhibited.Id} across a barrier or a call that suspends). " +
               "This build did not report it: build with the analyzers on, or jump to the first label of the chain.\");";
    }

    /// <summary>
    /// The lowered body of <paramref name="methodName"/> with the context accesses of its resume locals turned back into
    /// locals, the hoisted locals that share a name renamed and the labels that share a name renamed
    /// (<see cref="ContextLocalRewriter"/>).
    /// </summary>
    internal static BlockSyntax RewriteResumeLocals(BlockSyntax loweredBody,
        Dictionary<DeclarationKey, ManagedContextField> unhoistedLocals, Dictionary<DeclarationKey, string> renamedLocals,
        SemanticModel semanticModel, string methodName, bool zeroFill) =>
        ContextLocalRewriter.Rewrite(loweredBody, new ManagedResumeLocalSet(unhoistedLocals, zeroFill), renamedLocals,
            semanticModel, methodName) as BlockSyntax
        ?? throw new InvalidOperationException($"Unhoisting ref-struct fields removed the body of '{methodName}'.");

    /// <summary>
    /// Emits the frame class and the state machine of a device function that may suspend. The state machine returns
    /// whether the call completed; the caller calls it again on every resume until it does.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="plan"/> is not a <see cref="MethodEmissionKind.SuspendingDeviceFunction"/>.
    /// </exception>
    public static string GenerateDeviceFunction(in MethodEmissionPlan plan)
    {
        // A suspending device function always has a state machine and a symbol
        if (plan.Kind != MethodEmissionKind.SuspendingDeviceFunction || plan.Lowered is not { } lowered ||
            plan.Symbol is not { } methodSymbol)
        {
            throw new ArgumentException("Only a device function that may suspend has a managed state machine.",
                nameof(plan));
        }

        var methodDecl = plan.Declaration;
        var semanticModel = plan.SemanticModel;
        var frameName =
            GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.FrameTypePrefix, methodSymbol);
        var stateMachineName =
            GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.StateMachinePrefix, methodSymbol);
        var functionName = methodDecl.Identifier.ValueText;

        var (localInits, regularFields, unhoistedLocals, renamedLocals, slots) =
            ManagedContextFields.ProcessContextFields(
                lowered.ContextFields, semanticModel, functionName, lowered.LoweredBody);
        var copiedParameters = plan.GetCopiedParameters(lowered);
        var callees = GetCallees(lowered.CallSites, semanticModel);
        var frame = new StateMachineFrame(copiedParameters, slots, callees, lowered.WarpSiteCount);

        var isUnsafe = ManagedStateMachineAvailability.NeedsUnsafe(methodSymbol, methodDecl) ||
                       ManagedStateMachineEmitter.HasPointerField(frame);
        var compilation = semanticModel.Compilation;
        var accessors = new ManagedAccessorSet(methodSymbol, compilation);
        // The generator emits a state machine only when every callee has a frame (ManagedStateMachineAvailability)
        var callFrames = ManagedStateMachineAvailability.PlanCallFrames(callees,
                             methodSymbol.ContainingType, compilation, out _)
                         ?? throw new InvalidOperationException(
                             $"The managed state machine of '{functionName}' calls a function without a frame.");
        var bodySource = CreateBody(lowered, unhoistedLocals, renamedLocals, semanticModel, functionName,
            methodSymbol.AreLocalsZeroed(),
            new ManagedExecutionRewriter(semanticModel, ManagedBodyKind.SuspendingDeviceFunction, accessors, isUnsafe,
                callFrames));

        // As visible as the function, narrowed to the types its frame holds: the one answer that every caller uses
        // (ManagedStateMachineAvailability), so the frame, its entries and its callers cannot disagree
        var accessibility = ManagedStateMachineAvailability.GetFrameAccessibility(methodSymbol, compilation);
        using var writer = new SourceWriter();
        ManagedStateMachineEmitter.EmitContextClass(
            writer, frameName, frame, callFrames, accessibility, Accessibility.Internal,
            isUnsafe: isUnsafe);
        ManagedStateMachineEmitter.EmitDeviceStateMachineMethod(
            writer, frameName, stateMachineName, methodSymbol, frame, bodySource, accessibility,
            isUnsafe: isUnsafe);
        if (ManagedStateMachineAvailability.HasEntries(methodSymbol, accessibility))
        {
            // With the function's declared accessibility: the callers of other classes run the private frame through
            // them
            ManagedStateMachineEmitter.EmitFrameEntries(writer, frameName, stateMachineName, methodSymbol,
                frame.CopiedParameters, methodSymbol.DeclaredAccessibility, isUnsafe);
        }
        accessors.WriteTo(writer);
        ManagedWarningRestore.Append(writer, ManagedWarningRestore.Create(plan.Declaration));
        return writer.ToString();
    }

    /// <summary>
    /// Emits the managed version of a device function that does not suspend (#711): a static method with the same
    /// parameters, type parameters and accessibility, named <c>__gsharp_managed_&lt;function path&gt;</c>, whose body is
    /// the user's body after the managed rewrites (<see cref="ManagedExecutionRewriter"/>). The body is not lowered: it
    /// runs as one call, as the user's method does.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="plan"/> has no managed version (<see cref="MethodEmissionPlan.HasManagedVersion"/>).
    /// </exception>
    public static string GenerateManagedVersion(in MethodEmissionPlan plan)
    {
        if (!plan.HasManagedVersion || plan.Symbol is not { } methodSymbol)
        {
            throw new ArgumentException("Only a device function that needs a managed version has one.", nameof(plan));
        }

        return GenerateCopy(in plan, methodSymbol, GSharpInternalNames.ManagedVersion.MethodPrefix,
            ManagedBodyKind.ManagedVersion);
    }

    /// <summary>
    /// Emits the stack form of a kernel or a device function that reaches a block barrier or a warp collective: a
    /// static method with the user's parameters, named <c>__gsharp_stackmodel_&lt;path&gt;</c>, whose body is the
    /// user's body after the managed rewrites (<see cref="ManagedExecutionRewriter"/>). The body is not flattened into
    /// states: each GPU thread runs it on a stack of its own and waits inside the barrier or the collective. A method
    /// that reaches <c>Warp.ActiveMask()</c> writes the body of <see cref="MethodEmissionPlan.StackBody"/> instead of
    /// the user's, which holds the reconvergence regions of the state machine.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="plan"/> has no symbol or no body.
    /// </exception>
    public static string GenerateStackForm(in MethodEmissionPlan plan)
    {
        if (plan.Symbol is not { } methodSymbol ||
            (plan.Declaration.Body is null && plan.Declaration.ExpressionBody is null))
        {
            throw new ArgumentException("A stack form requires a method symbol with a body.", nameof(plan));
        }

        return GenerateCopy(in plan, methodSymbol, GSharpInternalNames.StackModel.MethodPrefix,
            ManagedBodyKind.StackBody);
    }

    private static string GenerateCopy(in MethodEmissionPlan plan, IMethodSymbol methodSymbol, string prefix,
        ManagedBodyKind bodyKind)
    {
        var declaration = plan.Declaration;
        var semanticModel = plan.SemanticModel;
        var accessors = new ManagedAccessorSet(methodSymbol, semanticModel.Compilation,
            isStackForm: bodyKind == ManagedBodyKind.StackBody);
        var isKernel = methodSymbol.HasAttribute(KnownAttributes.GpuKernel);
        var warpLayout = bodyKind == ManagedBodyKind.StackBody
            ? ManagedStackWarpLayout.Create(methodSymbol, declaration, semanticModel.Compilation, isKernel)
            : null;
        var rewriter = new ManagedExecutionRewriter(semanticModel, bodyKind, accessors) { WarpLayout = warpLayout };
        var stackBody = bodyKind == ManagedBodyKind.StackBody &&
                        SuspensionAnalysis.ReachesActiveMaskOnStack(methodSymbol, semanticModel.Compilation)
            ? GetStackBody(in plan, methodSymbol)
            : null;

        // <modifiers> [static|readonly] <return type> <prefix><path><type parameters>(<parameters>) <constraints> <body>
        // The signature is the user's ('static' when static, 'readonly' when present), with every name written in full
        // (the generated file has none of the user's using directives), without attributes (the version is not a device
        // function) and without 'this'
        var method = declaration
            .WithAttributeLists(default)
            .WithModifiers(GetManagedVersionModifiers(declaration, methodSymbol))
            .WithReturnType(ManagedNameQualifier.Qualify(declaration.ReturnType, semanticModel))
            .WithExplicitInterfaceSpecifier(null)
            .WithIdentifier(SyntaxFactory.Identifier(
                GlobalCppNames.GetKernelDerivedName(prefix, methodSymbol)))
            .WithTypeParameterList(GetManagedVersionTypeParameters(declaration.TypeParameterList))
            .WithParameterList(WithWarpFrameParameter(
                GetManagedVersionParameters(declaration.ParameterList, semanticModel), warpLayout, isKernel))
            .WithConstraintClauses(GetManagedVersionConstraints(declaration.ConstraintClauses, semanticModel));
        method = stackBody is null
            ? WriteBody(method, declaration, rewriter, semanticModel)
            : WriteStackBody(method, stackBody, rewriter, isKernel);
        if (warpLayout is not null && isKernel)
        {
            method = WithWarpBegin(method, warpLayout.Size, methodSymbol.ReturnsVoid);
        }

        method = method.WithoutTrivia();

        using var writer = new SourceWriter();
        writer.AppendLine(ManagedStateMachineEmitter.EditorBrowsableNever);
        writer.AppendLine(ManagedStateMachineEmitter.ToSource(method));
        writer.AppendLine();
        accessors.WriteTo(writer);
        ManagedWarningRestore.Append(writer, ManagedWarningRestore.Create(plan.Declaration));
        return writer.ToString();
    }

    // [WHY] The lanes of a method that reaches Warp.ActiveMask() wait for each other where a thread-dependent branch
    // ends, at the places where the state machine puts its regions: the lowering for the stacks runs the passes of the
    // state machine up to the regions (AstLoweringPipeline.LowerForStack), so both models meet in the same places
    private static BlockSyntax GetStackBody(in MethodEmissionPlan plan, IMethodSymbol methodSymbol) =>
        plan.StackBody?.Body ?? throw new InvalidOperationException(
            $"G#: internal error: '{methodSymbol.Name}' reaches Warp.ActiveMask() but has no body lowered for the " +
            "stack model (please report this G# bug).");

    // The lowered body rewritten for managed execution. It is already without Stryker's switches (the lowering removes
    // them), and a device function's is written without the statements that no path reaches, as its state machine is
    // (RewriteAndNormalizeBody)
    private static MethodDeclarationSyntax WriteStackBody(MethodDeclarationSyntax method, BlockSyntax stackBody,
        ManagedExecutionRewriter rewriter, bool isKernel)
    {
        var rewritten = rewriter.Visit(stackBody) as BlockSyntax
                        ?? throw new InvalidOperationException("Managed execution rewriting removed the stack body.");
        if (!isKernel && UnreachableStatementRemover.Instance.Visit(rewritten) is BlockSyntax reachable)
        {
            rewritten = reachable;
        }

        return method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(rewritten);
    }

    // The body of the copy: the user's body or expression body rewritten for managed execution, without Stryker's
    // switches when the compilation is mutated (#1191). A void expression body that is a null-conditional access
    // calling an extension method on a binding is a block body: its access is written as a statement on the receiver
    // in a pattern variable (ManagedConditionalAccess)
    private static MethodDeclarationSyntax WriteBody(MethodDeclarationSyntax method,
        MethodDeclarationSyntax declaration, ManagedExecutionRewriter rewriter, SemanticModel semanticModel)
    {
        if (declaration.ExpressionBody is { } arrow &&
            rewriter.TryRewriteBody(MutantSwitchRemover.Remove(arrow, semanticModel)) is { } accessBody)
        {
            return method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(accessBody);
        }

        return method
            .WithBody(declaration.Body is { } body ? Rewrite(body) : null)
            .WithExpressionBody(declaration.ExpressionBody is { } expressionBody ? Rewrite(expressionBody) : null);

        T Rewrite<T>(T node) where T : SyntaxNode =>
            rewriter.Visit(MutantSwitchRemover.Remove(node, semanticModel)) as T
            ?? throw new InvalidOperationException("Managed execution rewriting removed the device function body.");
    }

    // The user's accessibility, static, readonly, and unsafe when the version needs it (the partial declaration that
    // holds it is not inside the user's unsafe type); nothing else (partial, extern, new)
    private static SyntaxTokenList GetManagedVersionModifiers(MethodDeclarationSyntax declaration, IMethodSymbol method)
    {
        var modifiers = SyntaxFactory.TokenList();
        foreach (var modifier in declaration.Modifiers)
        {
            if (modifier.Kind() is SyntaxKind.PublicKeyword or SyntaxKind.InternalKeyword or SyntaxKind.PrivateKeyword
                or SyntaxKind.ProtectedKeyword or SyntaxKind.StaticKeyword or SyntaxKind.ReadOnlyKeyword)
            {
                modifiers = modifiers.Add(modifier.WithoutTrivia());
            }
        }

        if (ManagedStateMachineAvailability.NeedsUnsafe(method, declaration))
        {
            modifiers = modifiers.Add(SyntaxFactory.Token(SyntaxKind.UnsafeKeyword));
        }

        return modifiers;
    }

    // The user's parameters with their types and default values written in full, without attributes and without the
    // 'this' of an extension method (the version is called as a static method)
    private static ParameterListSyntax GetManagedVersionParameters(ParameterListSyntax parameters,
        SemanticModel semanticModel)
    {
        var qualified = ManagedNameQualifier.Qualify(parameters, semanticModel);
        var result = ImmutableArray.CreateBuilder<ParameterSyntax>(qualified.Parameters.Count);
        qualified.Parameters
            .AsStackEnumerable()
            .Select(static p => p
                .WithAttributeLists(default)
                .WithModifiers(RemoveThis(p.Modifiers))
            ).AddTo(result);
        return qualified.WithParameters(SyntaxFactory.SeparatedList(result.MoveToImmutable()));
    }

    // The stack form of a device function that reaches a warp collective takes the offset of its frame in the visit
    // counts first (int __gsharp_warp_frame, <parameters>): first, because C# allows no required parameter after an
    // optional one. A kernel declares it as a local instead (WithWarpBegin)
    private static ParameterListSyntax WithWarpFrameParameter(ParameterListSyntax parameters,
        ManagedStackWarpLayout? warpLayout, bool isKernel)
    {
        if (warpLayout is null || isKernel)
        {
            return parameters;
        }

        var frame = SyntaxFactory.Parameter(SyntaxFactory.Identifier(GSharpInternalNames.StackWarp.Frame))
            .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)));
        return parameters.WithParameters(parameters.Parameters.Insert(0, frame));
    }

    // The stack form of a kernel that reaches a warp collective clears the visit counts of its GPU thread and puts its
    // frame at 0 before its body:
    // { global::Kassyi.GSharp.Core.ManagedGpuContext.__gsharp_stack_warp_begin(<size>); const int __gsharp_warp_frame
    //     = 0; <body> }
    // An expression body becomes the statement or return that ends the block
    private static MethodDeclarationSyntax WithWarpBegin(MethodDeclarationSyntax method, int size, bool returnsVoid)
    {
        var sizeArgument = SyntaxFactory.Argument(
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(size))
        );
        var begin = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                ManagedNames.CreateMemberAccess(GSharpInternalNames.StackWarp.Begin),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(sizeArgument))
            )
        );
        var zero = SyntaxFactory.EqualsValueClause(
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))
        );
        var declarator = SyntaxFactory.VariableDeclarator(GSharpInternalNames.StackWarp.Frame).WithInitializer(zero);
        var frame = SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                SyntaxFactory.SingletonSeparatedList(declarator)
            )
        ).WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.ConstKeyword)));

        var statements = method.Body is { } body
            ? body.Statements
            : SyntaxFactory.SingletonList(ExpressionBodyStatement(
                method.ExpressionBody?.Expression ??
                throw new InvalidOperationException("A stack form of a kernel has neither a body nor an expression."),
                returnsVoid));
        return method
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithBody(SyntaxFactory.Block(statements.Insert(0, frame).Insert(0, begin)));
    }

    // <expression>; for a void method, throw <exception>; for a throw expression, return <expression>; otherwise
    private static StatementSyntax ExpressionBodyStatement(ExpressionSyntax expression, bool returnsVoid) =>
        expression switch
        {
            ThrowExpressionSyntax throwExpression => SyntaxFactory.ThrowStatement(throwExpression.Expression),
            _ when returnsVoid => SyntaxFactory.ExpressionStatement(expression),
            _ => SyntaxFactory.ReturnStatement(expression)
        };

    private static SyntaxTokenList RemoveThis(SyntaxTokenList modifiers)
    {
        var index = modifiers.IndexOf(SyntaxKind.ThisKeyword);
        return index < 0 ? modifiers : modifiers.RemoveAt(index);
    }

    // The type parameters without their attributes
    private static TypeParameterListSyntax? GetManagedVersionTypeParameters(TypeParameterListSyntax? typeParameters)
    {
        if (typeParameters is null)
        {
            return null;
        }

        var result = ImmutableArray.CreateBuilder<TypeParameterSyntax>(typeParameters.Parameters.Count);
        typeParameters.Parameters
            .AsStackEnumerable()
            .Select(static p => p.WithAttributeLists(default))
            .AddTo(result);
        return typeParameters.WithParameters(SyntaxFactory.SeparatedList(result.MoveToImmutable()));
    }

    // The constraint clauses with every name written in full
    private static SyntaxList<TypeParameterConstraintClauseSyntax> GetManagedVersionConstraints(
        SyntaxList<TypeParameterConstraintClauseSyntax> clauses, SemanticModel semanticModel)
    {
        var result = ImmutableArray.CreateBuilder<TypeParameterConstraintClauseSyntax>(clauses.Count);
        clauses
            .AsStackEnumerable()
            .Select(semanticModel, static (clause, model) => ManagedNameQualifier.Qualify(clause, model))
            .AddTo(result);
        return SyntaxFactory.List(result.MoveToImmutable());
    }

    // The source of the state machine body: the lowered body rewritten for managed execution, or only a throw when the
    // body holds a ref local (LoweredStateMachine.HeldRefLocals), which is an error of its own, or when it holds a goto
    // to an inner chained label from another state (GSH0121, or GSH0040 across a barrier)
    private static string CreateBody(LoweredStateMachine lowered,
        Dictionary<DeclarationKey, ManagedContextField> unhoistedLocals,
        Dictionary<DeclarationKey, string> renamedLocals,
        SemanticModel semanticModel, string methodName, bool zeroFill, ManagedExecutionRewriter rewriter)
    {
        // [WHY] Such a goto would stay a C# goto into another case block (CS0159). The generator reports it as an error
        // (GSH0121; across a barrier the analyzer reports GSH0040, ChainedLabelJumpChecks.FindReported), which stops the
        // build. A build without the analyzers does not stop at GSH0040, and an empty state machine would never set the
        // state to -1, so the launcher would loop for ever: the body throws at the jump's place instead
        if (!lowered.GotosToChainedLabelsAcrossStates.IsEmpty)
        {
            return CreateChainedLabelJumpThrow(methodName, lowered.GotosToChainedLabelsAcrossStates, semanticModel);
        }

        if (lowered.HeldRefLocals is [var heldRefLocal, ..])
        {
            return ManagedContextFields.CreateHeldRefLocalThrow(methodName, heldRefLocal.Declarator);
        }

        var body = RewriteResumeLocals(lowered.LoweredBody, unhoistedLocals, renamedLocals, semanticModel, methodName,
            zeroFill);
        return RewriteAndNormalizeBody(body, rewriter);
    }

    private static string RewriteAndNormalizeBody(BlockSyntax loweredBody, ManagedExecutionRewriter rewriter)
    {
        var isDeviceFunction = rewriter.BodyKind == ManagedBodyKind.SuspendingDeviceFunction;
        var rewritten = rewriter.Visit(loweredBody) as BlockSyntax
                        ?? throw new InvalidOperationException("Managed execution rewriting removed the kernel body.");
        // A device function's state machine is emitted by every assembly that exports its device code, so it is written
        // without the statements that no path reaches (CS0162 in a library that treats warnings as errors)
        if (isDeviceFunction && UnreachableStatementRemover.Instance.Visit(rewritten) is BlockSyntax reachable)
        {
            rewritten = reachable;
        }

        return ManagedStateMachineEmitter.ToSource(rewritten);
    }
}
