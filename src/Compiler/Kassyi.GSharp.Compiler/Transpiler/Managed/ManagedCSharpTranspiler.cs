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

        var (localInits, regularFields, unhoistedLocals, renamedLocals) =
            ManagedContextFields.ProcessContextFields(contextFields, semanticModel, kernelName);
        var copiedParameters = plan.GetCopiedParameters(lowered);

        var unhoistedBody = RewriteResumeLocals(loweredBody, unhoistedLocals, renamedLocals, semanticModel, kernelName,
            methodSymbol.AreLocalsZeroed());
        var accessors = new ManagedAccessorSet(methodSymbol, semanticModel.Compilation);
        var bodySource = RewriteAndNormalizeBody(unhoistedBody, semanticModel, isDeviceFunction: false, accessors);

        using var writer = new SourceWriter();
        ManagedStateMachineEmitter.EmitContextClass(
            writer, contextName, regularFields, copiedParameters, lowered.CallSites, semanticModel);
        ManagedStateMachineEmitter.EmitStateMachineMethod(
            writer, contextName, stateMachineName, methodSymbol, plan.HasAutoResult, copiedParameters, localInits,
            bodySource);
        accessors.WriteTo(writer);
        return writer.ToString();
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

        var (localInits, regularFields, unhoistedLocals, renamedLocals) =
            ManagedContextFields.ProcessContextFields(lowered.ContextFields, semanticModel, functionName);
        var copiedParameters = plan.GetCopiedParameters(lowered);

        var unhoistedBody = RewriteResumeLocals(lowered.LoweredBody, unhoistedLocals, renamedLocals, semanticModel,
            functionName, methodSymbol.AreLocalsZeroed());
        var accessors = new ManagedAccessorSet(methodSymbol, semanticModel.Compilation);
        var bodySource = RewriteAndNormalizeBody(unhoistedBody, semanticModel, isDeviceFunction: true, accessors);

        // As visible as the function (a consumer of another assembly calls a public one); the frame's fields are the
        // assembly's own (#538 3rd review M1)
        var accessibility = ManagedStateMachineAvailability.IsEffectivelyPublic(methodSymbol) ? "public" : "internal";
        using var writer = new SourceWriter();
        ManagedStateMachineEmitter.EmitContextClass(
            writer, frameName, regularFields, copiedParameters, lowered.CallSites, semanticModel, accessibility,
            "internal");
        ManagedStateMachineEmitter.EmitDeviceStateMachineMethod(
            writer, frameName, stateMachineName, methodSymbol, copiedParameters, localInits, bodySource, accessibility,
            ManagedStateMachineAvailability.NeedsUnsafe(methodSymbol, methodDecl));
        accessors.WriteTo(writer);
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

        var declaration = plan.Declaration;
        var semanticModel = plan.SemanticModel;
        var accessors = new ManagedAccessorSet(methodSymbol, semanticModel.Compilation);
        var rewriter = new ManagedExecutionRewriter(semanticModel, ManagedBodyKind.ManagedVersion, accessors);

        // <modifiers> static <return type> __gsharp_managed_<path><type parameters>(<parameters>) <constraints> <body>
        // The signature is the user's, with every name written in full (the generated file has none of the user's using
        // directives), without attributes (the version is not a device function) and without 'this'
        var method = declaration
            .WithAttributeLists(default)
            .WithModifiers(GetManagedVersionModifiers(declaration, methodSymbol))
            .WithReturnType(ManagedNameQualifier.Qualify(declaration.ReturnType, semanticModel))
            .WithExplicitInterfaceSpecifier(null)
            .WithIdentifier(SyntaxFactory.Identifier(
                GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.ManagedVersion.MethodPrefix, methodSymbol)))
            .WithTypeParameterList(GetManagedVersionTypeParameters(declaration.TypeParameterList))
            .WithParameterList(GetManagedVersionParameters(declaration.ParameterList, semanticModel))
            .WithConstraintClauses(GetManagedVersionConstraints(declaration.ConstraintClauses, semanticModel))
            .WithBody(declaration.Body is { } body
                ? rewriter.Visit(body) as BlockSyntax
                  ?? throw new InvalidOperationException("Managed execution rewriting removed the device function body.")
                : null)
            .WithExpressionBody(declaration.ExpressionBody is { } expressionBody
                ? rewriter.Visit(expressionBody) as ArrowExpressionClauseSyntax
                  ?? throw new InvalidOperationException("Managed execution rewriting removed the device function body.")
                : null)
            .WithoutTrivia();

        using var writer = new SourceWriter();
        writer.AppendLine(ManagedStateMachineEmitter.EditorBrowsableNever);
        writer.AppendLine(ManagedStateMachineEmitter.ToSource(method));
        writer.AppendLine();
        accessors.WriteTo(writer);
        return writer.ToString();
    }

    // The user's accessibility, static, and unsafe when the version needs it (the partial declaration that holds it is
    // not inside the user's unsafe type); nothing else (partial, extern, new)
    private static SyntaxTokenList GetManagedVersionModifiers(MethodDeclarationSyntax declaration, IMethodSymbol method)
    {
        var modifiers = SyntaxFactory.TokenList();
        foreach (var modifier in declaration.Modifiers)
        {
            if (modifier.Kind() is SyntaxKind.PublicKeyword or SyntaxKind.InternalKeyword or SyntaxKind.PrivateKeyword
                or SyntaxKind.ProtectedKeyword or SyntaxKind.StaticKeyword)
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

    private static string RewriteAndNormalizeBody(BlockSyntax loweredBody, SemanticModel semanticModel,
        bool isDeviceFunction, ManagedAccessorSet accessors)
    {
        var rewriter = new ManagedExecutionRewriter(semanticModel,
            isDeviceFunction ? ManagedBodyKind.SuspendingDeviceFunction : ManagedBodyKind.Kernel, accessors);
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
