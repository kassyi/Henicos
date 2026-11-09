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
    /// The reason that <paramref name="call"/> of <paramref name="called"/> cannot be emitted, or
    /// <see langword="null"/> when the callee is emitted, mapped or declared outside the compilation, or when C#
    /// removes the call (<c>[Conditional]</c> without its symbol defined at the call).
    /// </summary>
    public static string? GetReason(IMethodSymbol called, SyntaxNode call, SemanticModel model,
        ITranspilerDialect dialect)
    {
        // [WHY] Decided on the user's own tree only, which the CUDA run (and so the analyzer) transpiles. A lowered
        // tree of the C++ run rewrites calls (a suspending call gains its frame) and binds them through the
        // original-span mapping, which does not always find the call that was written
        if (call.SyntaxTree != model.SyntaxTree)
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
            method.IsPartialWithoutImplementation() ||
            !IsDeclaredIn(method, model.Compilation) ||
            dialect.TryMapIntrinsic(called, out _) ||
            BclIntrinsicMapBuilder.GetOrBuildMap(model.Compilation).ContainsKey(method) ||
            ConditionalCalls.IsRemoved(method, call))
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
