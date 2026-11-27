namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Checks the signature of a kernel or device function: its return type (GSH0004), an instance device function
/// (GSH0066), multidimensional array, function pointer and device-function heap object parameters (GSH0003), and the
/// CUDA kernel parameter space (GSH0064).
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

        if (!isDeviceIteratorCandidate &&
            GpuCodeConstraints.GetInstanceDeviceFunctionProblem(method, context.SemanticModel.Compilation) is { } reason)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.InstanceDeviceFunctionNotSupported,
                node.Identifier.GetLocation(),
                method.ContainingType.Name,
                method.Name,
                reason);
        }

        var isKernel = method.HasAttribute(KnownAttributes.GpuKernel);
        foreach (var parameter in method.Parameters)
        {
            if (parameter.TryGetDeclaringSyntax<ParameterSyntax>(out var parameterSyntax) &&
                parameterSyntax.Type is { } typeSyntax)
            {
                ReportParameterType(parameter.Type, typeSyntax, isKernel, ref context);
            }
        }

        if (isKernel)
        {
            CheckReservedLaunchParameterNames(method, ref context);
            CheckKernelParameterSpace(node, method, ref context);
        }
    }

    // A kernel parameter of a heap object type is GSH0112 (KernelDeclarationAnalyzer), so a kernel is not reported again
    private static void ReportParameterType(ITypeSymbol type, TypeSyntax typeSyntax, bool isKernel,
        ref ValidationContext context)
    {
        var message = type switch
        {
            _ when GpuCodeConstraints.ContainsMultidimensionalArray(type) => GpuCodeConstraints.MultidimensionalArray,
            _ when GpuCodeConstraints.IsFunctionPointer(type) => typeSyntax.Kind().ToString(),
            _ when !isKernel && GpuCodeConstraints.IsHeapObjectType(type) => GpuCodeConstraints.HeapObjectType,
            _ => null
        };

        if (message is not null)
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, typeSyntax, message);
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
        // A kernel whose in parameter may share memory with another parameter is emitted twice, with the in parameter
        // by value and by address, and each entry point must fit: the larger of the two is checked
        var hidden = KernelHiddenParameters.For(node, kernel, context.SemanticModel, isCuda: true);
        var offset = Math.Max(
            hidden.AppendTo(GetParameterSpace(kernel, inByAddress: false)),
            hidden.AppendTo(GetParameterSpace(kernel, inByAddress: true))
        );

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

    // The bytes of the declared parameters in one entry point: ref and out by pointer always, an in parameter that may
    // share memory by pointer only in the by-address entry point (SymbolExtensions.IsKernelInPassedByAddress)
    private static int GetParameterSpace(IMethodSymbol kernel, bool inByAddress)
    {
        var offset = 0;
        foreach (var parameter in kernel.Parameters)
        {
            var byPointer = parameter.RefKind is RefKind.Ref or RefKind.Out ||
                (inByAddress && parameter.IsKernelInPassedByAddress());
            var layout = byPointer
                ? new TypeLayout(DevicePointerBytes, DevicePointerBytes)
                : TypeLayoutCalculator.GetDeviceLayout(parameter.Type.UnwrapNullableArrayLike() ?? parameter.Type, s_cudaDialect);

            offset = TypeLayoutCalculator.AlignUp(offset, layout.Alignment) + layout.Size;
        }

        return offset;
    }
}
