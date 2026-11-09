namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a call from GPU code of a method of the compilation that G# does not emit (GSH0107): an ordinary method
/// without <c>[GpuDeviceFunction]</c>, which the device code would call by a name that nothing defines.
/// </summary>
/// <remarks>
/// [WHY] Only <c>[GpuDeviceFunction]</c> static methods are emitted for the device
/// (<see cref="CppNameResolver.IsEmittedDeviceFunction"/>); the attribute is the marker that the semantics charter asks
/// for before G# compiles a method for the GPU. An instance method of a struct has no device form yet (GSH0066), so its
/// message says so instead of asking for the attribute. A method the dialect maps (an intrinsic, <c>[CudaImport]</c>,
/// an operator macro) and a partial method without an implementation (the call is removed) are not reported.
/// </remarks>
internal sealed class UnmarkedMethodCallRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly UnmarkedMethodCallRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode);

    private UnmarkedMethodCallRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is not IMethodSymbol called)
        {
            return;
        }

        var method = (called.ReducedFrom ?? called).OriginalDefinition;
        if (!IsUnemittedSourceMethod(method, context.SemanticModel.Compilation, context.Dialect))
        {
            return;
        }

        var name = method.ContainingType.Name + "." + method.Name;
        var reason = method is { IsStatic: false, ContainingType.IsValueType: true }
            ? "instance methods of structs are not supported in GPU code yet (" +
              GSharpDiagnostics.InstanceDeviceFunctionNotSupported.Id +
              "); make it a static [GpuDeviceFunction] method that takes the struct as a parameter"
            : "only [GpuDeviceFunction] methods are compiled for the GPU; add [GpuDeviceFunction] to it";
        context.ReportDiagnostic(GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode, node, name, reason);
    }

    // An ordinary method declared in this compilation that G# neither emits nor maps
    private static bool IsUnemittedSourceMethod(IMethodSymbol method, Compilation compilation,
        ITranspilerDialect dialect) =>
        method.MethodKind == MethodKind.Ordinary &&
        !method.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
        !method.HasAttribute(KnownAttributes.GpuKernel) &&
        !method.HasAttribute(KnownAttributes.CudaIntrinsic) &&
        !method.HasAttribute(KnownAttributes.CudaImport) &&
        !method.HasAttribute(KnownAttributes.GpuOperatorMacro) &&
        method.ContainingType?.HasAttribute(KnownAttributes.CudaIntrinsicPattern) != true &&
        !method.IsPartialWithoutImplementation() &&
        method.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(compilation, static (reference, c) => c.ContainsSyntaxTree(reference.SyntaxTree)) &&
        !dialect.TryMapIntrinsic(method, out _);
}
