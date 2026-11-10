namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Decides whether a call that reaches the standard call emission names a method of the compilation that has no
/// device definition (GSH0107, #891), and why. The transpiler reports it, and the analyzer reports it through the
/// transpiler (<c>TranspilerFailureCheck</c>), so the decision exists once.
/// </summary>
/// <remarks>
/// [WHY] Only <c>[GpuDeviceFunction]</c> static methods are emitted
/// (<see cref="CppNameResolver.IsEmittedDeviceFunction"/>). The calls that other rules write before the standard
/// emission (GPU macros, StackLinq, span members, <c>[BclIntrinsicMap]</c>, which is also checked here) never reach
/// this decision, and a method the dialect maps (<c>[CudaIntrinsic]</c>, an intrinsic pattern, <c>[CudaImport]</c>, an
/// operator macro) is written under its native name.
/// </remarks>
internal static class UnemittedMethodCall
{
    /// <summary>The reason given for a static method without the attribute.</summary>
    public const string MissingAttribute =
        "only [GpuDeviceFunction] methods are compiled for the GPU; add [GpuDeviceFunction] to it";

    /// <summary>The reason given for an interface or abstract method.</summary>
    public const string ThroughInterface =
        "calls through an interface or an abstract method are not supported in GPU code; call a static " +
        "[GpuDeviceFunction] method of the concrete type instead";

    /// <summary>The reason given for an instance method of a struct.</summary>
    public static string InstanceMethodOfStruct =>
        "instance methods of structs are not supported in GPU code yet (" +
        GSharpDiagnostics.InstanceDeviceFunctionNotSupported.Id +
        "); make it a static [GpuDeviceFunction] method that takes the struct as a parameter";

    /// <summary>
    /// The reason that <paramref name="call"/> of <paramref name="called"/> (an invocation, or a method group that
    /// device code names) cannot be emitted, or
    /// <see langword="null"/> when the callee is emitted, mapped or declared outside the compilation, or when C#
    /// removes the call (<see cref="RemovedCalls"/>).
    /// </summary>
    public static string? GetReason(IMethodSymbol called, SyntaxNode call, SemanticModel model,
        ITranspilerDialect dialect)
    {
        // [WHY] A call copied into a lowered tree (an inlined iterator or foreach body, a state machine) is decided as
        // well, on the file it was written in, which its original-span annotation names; a call that lowering
        // synthesized has no written call to report
        if (RemovedCalls.GetUserTree(call, model) is not { } userTree)
        {
            return null;
        }

        var method = (called.ReducedFrom ?? called).OriginalDefinition;
        if (method.MethodKind != MethodKind.Ordinary ||
            method.HasAttribute(KnownAttributes.GpuDeviceFunction) ||
            method.HasAttribute(KnownAttributes.GpuKernel) ||
            method.HasAttribute(KnownAttributes.CudaIntrinsic) ||
            method.HasAttribute(KnownAttributes.CudaImport) ||
            method.HasAttribute(KnownAttributes.GpuOperatorMacro) ||
            method.ContainingType?.HasAttribute(KnownAttributes.CudaIntrinsicPattern) == true ||
            RemovedCalls.IsRemoved(method, userTree) ||
            !IsDeclaredIn(method, model.Compilation) ||
            dialect.TryMapIntrinsic(called, out _) ||
            BclIntrinsicMapBuilder.GetOrBuildMap(model.Compilation).ContainsKey(method))
        {
            return null;
        }

        if (method.IsAbstract || method.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return ThroughInterface;
        }

        return method is { IsStatic: false, ContainingType.IsValueType: true }
            ? InstanceMethodOfStruct
            : MissingAttribute;
    }

    /// <summary>
    /// Reports GSH0107 and writes the placeholder when <paramref name="node"/> names a method without calling it (a
    /// method group passed to a StackLinq stage or converted to a delegate) whose method <see cref="GetReason"/>
    /// rejects; the device code would otherwise name a function nothing defines.
    /// </summary>
    /// <returns>Whether it reported and wrote the placeholder.</returns>
    public static bool TryFailMethodGroup(SyntaxNode node, ISymbol? symbol, ref TranspileContext context)
    {
        if (symbol is not IMethodSymbol method ||
            !IsMethodGroup(node) ||
            GetReason(method, node, context.Query.SemanticModel, context.Emitter.Dialect) is not { } reason)
        {
            return false;
        }

        context.Reporter.Fail(GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode, node, GetDisplayName(method), reason);
        context.Visitor.AppendUnsupportedPlaceholder(node);
        return true;
    }

    // A name of a method that is not the target of an invocation (U.F, F), outside nameof
    private static bool IsMethodGroup(SyntaxNode node)
    {
        var name = node.Parent is MemberAccessExpressionSyntax access && access.Name == node ? access : node;
        if (name.Parent is InvocationExpressionSyntax invocation && invocation.Expression == name)
        {
            return false;
        }

        return !name.Ancestors()
            .AsStackEnumerable()
            .Any(static ancestor => ancestor is InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }
            });
    }

    /// <summary>The name of <paramref name="called"/> in the message: <c>Type.Method</c>.</summary>
    public static string GetDisplayName(IMethodSymbol called)
    {
        var method = (called.ReducedFrom ?? called).OriginalDefinition;
        return method.ContainingType is { } type ? type.Name + "." + method.Name : method.Name;
    }

    private static bool IsDeclaredIn(IMethodSymbol method, Compilation compilation) =>
        method.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(compilation, static (reference, c) => c.ContainsSyntaxTree(reference.SyntaxTree));
}
