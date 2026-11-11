namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Plans the launcher of a <c>[CudaImport]</c> method from the symbols of its parameters. <c>CudaImportAnalyzer</c>
/// reports the problems of the plan (GSH0110) and <see cref="CudaImportGenerator"/> writes the plan, so both read the
/// same decision.
/// </summary>
internal static class CudaImportPlanner
{
    public static ModuleKernelLaunchPlan Plan(IMethodSymbol method, Compilation compilation)
    {
        var builder = new ModuleKernelLaunchPlanBuilder(method.Parameters.Length);
        foreach (var parameter in method.Parameters)
        {
            var launchSetting = ModuleKernelParameterTypes.GetLaunchSetting(parameter.Name);
            if (GetProblem(parameter, launchSetting, compilation) is { } reason)
            {
                builder.AddProblem(parameter.Ordinal, parameter.Name, IteratorTypeResolver.FormatType(parameter.Type),
                    reason);
            }
            else if (launchSetting != ModuleLaunchSetting.None)
            {
                builder.AddLaunchSetting(launchSetting, parameter.Name);
            }
            else
            {
                builder.AddArgument(CreateArgument(parameter));
            }
        }

        return builder.ToPlan();
    }

    /// <summary>Gets the GSH0110 message of <paramref name="problem"/>, which the launcher also throws.</summary>
    public static string FormatProblem(string methodName, ModuleKernelProblem problem) =>
        string.Format(
            CultureInfo.InvariantCulture,
            GSharpDiagnostics.CudaImportParameterNotSupported.MessageFormat.ToString(CultureInfo.InvariantCulture),
            problem.ParameterName,
            methodName,
            problem.TypeName,
            problem.Reason
        );

    /// <summary>Gets the method as the messages of its launcher name it (<c>Type.Method</c>).</summary>
    public static string GetMessageName(IMethodSymbol method) => $"{method.ContainingType.Name}.{method.Name}";

    private static ModuleKernelArgument CreateArgument(IParameterSymbol parameter)
    {
        var type = parameter.Type;
        var kind = type switch
        {
            IPointerTypeSymbol or IFunctionPointerTypeSymbol => ModuleKernelParameterKind.Pointer,
            IArrayTypeSymbol => ModuleKernelParameterKind.Array,
            _ => ModuleKernelParameterKind.Value,
        };

        using var typeWriter = new SourceWriter();
        typeWriter.AppendCsharpTypeName(type is IArrayTypeSymbol array ? array.ElementType : type);
        var hasIn = parameter.HasAttribute(KnownAttributes.In);
        var hasOut = parameter.HasAttribute(KnownAttributes.Out);
        return new ModuleKernelArgument(
            parameter.Name,
            kind,
            typeWriter.ToString(),
            CopyIn: hasIn || !hasOut,
            CopyOut: hasOut || !hasIn
        );
    }

    // Why the launcher cannot pass <parameter>, or null when it can (the table of ModuleKernelParameterTypes)
    private static string? GetProblem(IParameterSymbol parameter, ModuleLaunchSetting launchSetting,
        Compilation compilation)
    {
        if (ModuleKernelParameterTypes.IsReservedName(parameter.Name))
        {
            return ModuleKernelParameterTypes.ReservedNameReason;
        }

        if (parameter.RefKind != RefKind.None || parameter.ScopedKind != ScopedKind.None)
        {
            return ModuleKernelParameterTypes.ByReferenceReason;
        }

        var type = parameter.Type;
        if (type.TypeKind == TypeKind.Error)
        {
            // The compiler already reports the type; the launcher passes it as written
            return null;
        }

        if (launchSetting != ModuleLaunchSetting.None)
        {
            return GetLaunchSettingProblem(type, launchSetting, compilation);
        }

        return type switch
        {
            IPointerTypeSymbol or IFunctionPointerTypeSymbol => null,
            IArrayTypeSymbol array => GetArrayProblem(array),
            { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } =>
                ModuleKernelParameterTypes.NullableValueReason,
            { IsUnmanagedType: false } => ModuleKernelParameterTypes.ManagedTypeReason,
            _ => null,
        };
    }

    private static string? GetLaunchSettingProblem(ITypeSymbol type, ModuleLaunchSetting launchSetting,
        Compilation compilation)
    {
        if (launchSetting == ModuleLaunchSetting.Stream)
        {
            return type.SpecialType == SpecialType.System_IntPtr ? null : ModuleKernelParameterTypes.StreamTypeReason;
        }

        // [WHY] The launcher passes the parameter where cuLaunchKernel and the launch check take a uint
        var conversion = compilation.ClassifyConversion(type, compilation.GetSpecialType(SpecialType.System_UInt32));
        return conversion is { IsImplicit: true, IsUserDefined: false }
            ? null
            : ModuleKernelParameterTypes.DimensionTypeReason;
    }

    private static string? GetArrayProblem(IArrayTypeSymbol array)
    {
        var element = array.ElementType;
        if (element is IArrayTypeSymbol)
        {
            return ModuleKernelParameterTypes.JaggedArrayReason;
        }

        if (array.Rank > 1)
        {
            return ModuleKernelParameterTypes.MultidimensionalArrayReason;
        }

        if (array.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return ModuleKernelParameterTypes.NullableArrayReason;
        }

        return element switch
        {
            { TypeKind: TypeKind.Error } => null,
            IPointerTypeSymbol or IFunctionPointerTypeSymbol => ModuleKernelParameterTypes.PointerElementReason,
            { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } =>
                ModuleKernelParameterTypes.NullableValueReason,
            { IsUnmanagedType: false } => ModuleKernelParameterTypes.ManagedElementReason,
            _ => null,
        };
    }
}
