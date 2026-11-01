namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Checks the signature of a kernel or device function: its return type (GSH0004), an instance device function
/// (GSH0066), function pointer parameters (GSH0003) and the CUDA kernel parameter space (GSH0064).
/// </summary>
internal sealed class KernelSignatureRule : ISyntaxValidationRule<MethodDeclarationSyntax>
{
    public static readonly KernelSignatureRule Instance = new();

    // [WHY] CUDA limits the parameters of a kernel to 4 KB (4096 bytes).
    private const int KernelParameterSpaceLimitBytes = 4096;

    private const int DevicePointerBytes = 8;

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable,
        GSharpDiagnostics.InstanceDeviceFunctionNotSupported,
        GSharpDiagnostics.UnsupportedSyntax,
        GSharpDiagnostics.KernelParameterSpaceExceeded);

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

        if (!isDeviceIteratorCandidate && !GpuCodeConstraints.HasSupportedReturnType(method))
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
                var parameterType = parameter.Type.UnwrapNullableArrayLike() ?? parameter.Type;
                if (parameterType is IArrayTypeSymbol { Rank: > 1 } ||
                    (parameterType.TryGetArrayLikeElementType(out var elementType) &&
                     elementType is IArrayTypeSymbol { Rank: > 1 }) ||
                    (StructLayoutRule.TryGetUserStruct(parameter.Type, out var structType, out _) &&
                     StructLayoutRule.TryFindMultidimensionalArrayField(structType)))
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
            CheckKernelParameterSpace(node, method, ref context);
        }
    }

    private static void CheckKernelParameterSpace(MethodDeclarationSyntax node, IMethodSymbol kernel,
        ref ValidationContext context)
    {
        var offset = 0;
        foreach (var parameter in kernel.Parameters)
        {
            var layout = parameter.RefKind != RefKind.None
                ? new TypeLayout(DevicePointerBytes, DevicePointerBytes)
                : TypeLayoutCalculator.GetDeviceLayout(parameter.Type.UnwrapNullableArrayLike() ?? parameter.Type, context.Dialect);

            offset = TypeLayoutCalculator.AlignUp(offset, layout.Alignment) + layout.Size;
        }

        // A kernel that returns a value takes the pointer to its result buffer as a hidden last parameter
        if (!kernel.ReturnsVoid)
        {
            offset = TypeLayoutCalculator.AlignUp(offset, DevicePointerBytes) + DevicePointerBytes;
        }

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
