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
        using var typeWriter = new SourceWriter();
        typeWriter.AppendCsharpTypeName(type is IArrayTypeSymbol array ? array.ElementType : type);
        if (parameter.RefKind != RefKind.None)
        {
            // 'out' has no value to copy in; 'in' and 'ref readonly' promise not to change the caller's value
            return new ModuleKernelArgument(
                parameter.Name,
                ModuleKernelParameterKind.ByReference,
                typeWriter.ToString(),
                CopyIn: parameter.RefKind != RefKind.Out,
                CopyOut: parameter.RefKind is RefKind.Ref or RefKind.Out
            );
        }

        var kind = type switch
        {
            IPointerTypeSymbol or IFunctionPointerTypeSymbol => ModuleKernelParameterKind.Pointer,
            IArrayTypeSymbol { Rank: > 1 } => ModuleKernelParameterKind.MultidimensionalArray,
            IArrayTypeSymbol => ModuleKernelParameterKind.Array,
            _ => ModuleKernelParameterKind.Value,
        };

        var hasIn = parameter.HasAttribute(KnownAttributes.In);
        var hasOut = parameter.HasAttribute(KnownAttributes.Out);
        return new ModuleKernelArgument(
            parameter.Name,
            kind,
            typeWriter.ToString(),
            CopyIn: hasIn || !hasOut,
            CopyOut: hasOut || !hasIn,
            AllowsNull: type is IArrayTypeSymbol { NullableAnnotation: NullableAnnotation.Annotated }
        );
    }

    // Why the launcher cannot pass <parameter>, or null when it can (the table of ModuleKernelParameterTypes)
    private static string? GetProblem(IParameterSymbol parameter, ModuleLaunchSetting launchSetting,
        Compilation compilation)
    {
        var type = parameter.Type;
        if (type.TypeKind == TypeKind.Error)
        {
            // The compiler already reports the type; the launcher passes it as written
            return null;
        }

        if (type.IsRefLikeType)
        {
            return ModuleKernelParameterTypes.RefStructReason;
        }

        if (parameter.RefKind != RefKind.None)
        {
            return GetByReferenceProblem(type, launchSetting);
        }

        if (launchSetting != ModuleLaunchSetting.None)
        {
            return GetLaunchSettingProblem(type, launchSetting, compilation);
        }

        return type switch
        {
            IPointerTypeSymbol or IFunctionPointerTypeSymbol => null,
            IArrayTypeSymbol array => GetArrayProblem(array),
            _ => GetValueProblem(type),
        };
    }

    private static string? GetLaunchSettingProblem(ITypeSymbol type, ModuleLaunchSetting launchSetting,
        Compilation compilation)
    {
        if (launchSetting == ModuleLaunchSetting.Stream)
        {
            return type.SpecialType == SpecialType.System_IntPtr ? null : ModuleKernelParameterTypes.StreamTypeReason;
        }

        // [WHY] The launcher passes the parameter where cuLaunchKernel and the launch check take a uint; a user-defined
        // implicit conversion is applied there as in any C# call
        var conversion = compilation.ClassifyConversion(type, compilation.GetSpecialType(SpecialType.System_UInt32));
        return conversion.IsImplicit ? null : ModuleKernelParameterTypes.DimensionTypeReason;
    }

    // Why a 'ref', 'out' or 'in' parameter of <type> cannot be staged through a one-element device buffer
    private static string? GetByReferenceProblem(ITypeSymbol type, ModuleLaunchSetting launchSetting)
    {
        if (launchSetting != ModuleLaunchSetting.None)
        {
            return ModuleKernelParameterTypes.LaunchSettingByReferenceReason;
        }

        return type is IPointerTypeSymbol or IFunctionPointerTypeSymbol or IArrayTypeSymbol
            ? ModuleKernelParameterTypes.ByReferenceReason
            : GetValueProblem(type);
    }

    private static string? GetArrayProblem(IArrayTypeSymbol array)
    {
        var element = array.ElementType;
        if (element is IArrayTypeSymbol)
        {
            return ModuleKernelParameterTypes.JaggedArrayReason;
        }

        if (element is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
        {
            return ModuleKernelParameterTypes.PointerElementReason;
        }

        var problem = GetValueProblem(element);
        return problem == ModuleKernelParameterTypes.ManagedTypeReason
            ? ModuleKernelParameterTypes.ManagedElementReason
            : problem;
    }

    // Why a value of <type> (not a pointer or an array) cannot be copied to the kernel byte for byte
    private static string? GetValueProblem(ITypeSymbol type) => type switch
    {
        { TypeKind: TypeKind.Error } => null,
        { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } =>
            ModuleKernelParameterTypes.NullableValueReason,
        { IsRefLikeType: true } => ModuleKernelParameterTypes.RefStructReason,
        { IsUnmanagedType: false } => ModuleKernelParameterTypes.ManagedTypeReason,

        // [WHY] An unmanaged struct whose host layout is not known has LayoutKind.Auto itself, or in a field of a
        // field: ValueTuple, DateTime and a struct with [StructLayout(LayoutKind.Auto)] (HostLayoutCalculator)
        _ when !HostLayoutCalculator.TryGetHostLayout(type, out _) => ModuleKernelParameterTypes.AutoLayoutReason,
        _ => null,
    };
}
