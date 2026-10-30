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

        var unhoistedBody = RewriteResumeLocals(loweredBody, unhoistedLocals, renamedLocals, semanticModel, kernelName,
            methodSymbol.AreLocalsZeroed());
        var bodySource = RewriteAndNormalizeBody(unhoistedBody, semanticModel, isDeviceFunction: false);

        using var writer = new SourceWriter();
        ManagedStateMachineEmitter.EmitContextClass(
            writer, contextName, regularFields, ImmutableArray<IParameterSymbol>.Empty, lowered.CallSites,
            semanticModel);
        ManagedStateMachineEmitter.EmitStateMachineMethod(
            writer, contextName, stateMachineName, methodSymbol, plan.HasAutoResult, localInits, bodySource);
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
        var bodySource = RewriteAndNormalizeBody(unhoistedBody, semanticModel, isDeviceFunction: true);

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
        return writer.ToString();
    }

    private static string RewriteAndNormalizeBody(BlockSyntax loweredBody, SemanticModel semanticModel,
        bool isDeviceFunction)
    {
        var rewriter = new ManagedExecutionRewriter(semanticModel, isDeviceFunction);
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
