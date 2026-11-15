namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Checks the signature of a kernel or device function: its return type (GSH0004), an instance device function
/// (GSH0066), function pointer parameters (GSH0003) and the CUDA kernel parameter space (GSH0064).
/// </summary>
internal sealed class KernelSignatureRule : ISyntaxValidationRule<MethodDeclarationSyntax>
{
    public static readonly KernelSignatureRule Instance = new();

    // [WHY] The parameter space of a kernel is 32,764 bytes from CUDA 12.1 on Volta and newer, and 4,096 bytes before.
    // G# builds with CUDA 13.0 or newer, whose oldest target is compute capability 7.5, so every build G# supports has
    // the larger limit, and nvcc rejects a kernel above it ("Formal parameter space overflowed ... max 32764 bytes").
    private const int KernelParameterSpaceLimitBytes = 32764;

    private const int DevicePointerBytes = 8;

    // The limit is the CUDA one, so every user parameter is laid out as CUDA lays it out, whatever dialect the build has
    private static readonly ITranspilerDialect s_cudaDialect = new GSharpCudaDialect();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable,
        GSharpDiagnostics.InstanceDeviceFunctionNotSupported,
        GSharpDiagnostics.UnsupportedSyntax,
        GSharpDiagnostics.KernelParameterSpaceExceeded,
        GSharpDiagnostics.KernelParameterConflictsWithLaunchParameter);

    private KernelSignatureRule()
    {
    }

    public void Analyze(MethodDeclarationSyntax node, ref ValidationContext context)
    {
        var method = context.MethodSymbol;
        var isDeviceIteratorCandidate =
            method.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
            !method.HasAttribute(KnownAttributes.GpuKernel) &&
            IteratorTypeResolver.ContainsDirectYield(node.Body);

        if (!isDeviceIteratorCandidate && !GpuCodeConstraints.HasSupportedReturnType(method, context.Dialect))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable,
                method.Locations.Length > 0 ? method.Locations[0] : Location.None,
                method.Name,
                GpuCodeConstraints.FormatReturnType(method));
        }

        if (!isDeviceIteratorCandidate && GpuCodeConstraints.IsInstanceDeviceFunction(method))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.InstanceDeviceFunctionNotSupported,
                node.Identifier.GetLocation(),
                method.ContainingType.Name,
                method.Name);
        }

        foreach (var parameter in method.Parameters)
        {
            if (parameter.TryGetDeclaringSyntax<ParameterSyntax>(out var parameterSyntax) &&
                parameterSyntax.Type is { } typeSyntax)
            {
                if (GpuCodeConstraints.ContainsMultidimensionalArray(parameter.Type))
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.UnsupportedSyntax,
                        typeSyntax,
                        GpuCodeConstraints.MultidimensionalArray);
                }
                else if (GpuCodeConstraints.IsFunctionPointer(parameter.Type))
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.UnsupportedSyntax,
                        typeSyntax,
                        typeSyntax.Kind().ToString());
                }
            }
        }

        if (method.HasAttribute(KnownAttributes.GpuKernel))
        {
            CheckReservedLaunchParameterNames(method, ref context);
            CheckKernelParameterSpace(node, method, ref context);
        }
    }

    private static void CheckReservedLaunchParameterNames(IMethodSymbol kernel, ref ValidationContext context)
    {
        foreach (var parameter in kernel.Parameters)
        {
            if (KernelHostNames.IsReservedLaunchParameterName(parameter.Name) &&
                parameter.TryGetDeclaringSyntax<ParameterSyntax>(out var parameterSyntax))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.KernelParameterConflictsWithLaunchParameter,
                    parameterSyntax.Identifier.GetLocation(),
                    parameter.Name,
                    $"{kernel.ContainingType.Name}.{kernel.Name}",
                    KernelHostNames.LaunchOptionNames);
            }
        }
    }

    private static void CheckKernelParameterSpace(MethodDeclarationSyntax node, IMethodSymbol kernel,
        ref ValidationContext context)
    {
        var offset = 0;
        foreach (var parameter in kernel.Parameters)
        {
            var layout = parameter.IsPassedByPointer(isKernel: true)
                ? new TypeLayout(DevicePointerBytes, DevicePointerBytes)
                : TypeLayoutCalculator.GetDeviceLayout(parameter.Type.UnwrapNullableArrayLike() ?? parameter.Type, s_cudaDialect);

            offset = TypeLayoutCalculator.AlignUp(offset, layout.Alignment) + layout.Size;
        }

        // The parameters G# appends on CUDA (result buffer, grid aggregate scratch, launch epoch), where the limit
        // holds; a CPU dialect build of the same kernel is checked against the GPU it is meant for
        offset = KernelHiddenParameters.For(node, kernel, context.SemanticModel, isCuda: true).AppendTo(offset);

        if (offset > KernelParameterSpaceLimitBytes)
        {
            // The message names the kernel as the user wrote it, not by its entry point.
            context.ReportDiagnostic(
                GSharpDiagnostics.KernelParameterSpaceExceeded,
                node.Identifier.GetLocation(),
                $"{kernel.ContainingType.Name}.{kernel.Name}",
                offset,
                KernelParameterSpaceLimitBytes);
        }
    }
}
