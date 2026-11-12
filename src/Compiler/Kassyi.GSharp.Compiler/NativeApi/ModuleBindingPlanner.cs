namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Plans the launcher of a method of a YAML <c>moduleName</c> binding from the parameter types as written, read by
/// <see cref="ModuleKernelParameterTypes.Parse"/>, the same reading that validated them.
/// </summary>
internal static class ModuleBindingPlanner
{
    public static ModuleKernelLaunchPlan Plan(ApiMethodDefinition method)
    {
        var builder = new ModuleKernelLaunchPlanBuilder(method.Parameters.Length);
        for (var ordinal = 0; ordinal < method.Parameters.Length; ordinal++)
        {
            var parameter = method.Parameters[ordinal];
            var type = ModuleKernelParameterTypes.Parse(parameter.Name, parameter.Type);
            if (type.Reason is { } reason)
            {
                builder.AddProblem(ordinal, parameter.Name, type.TypeName, reason);
            }
            else if (type.LaunchSetting != ModuleLaunchSetting.None)
            {
                builder.AddLaunchSetting(type.LaunchSetting, parameter.Name.ToCSharpIdentifier());
            }
            else
            {
                // A YAML binding has no [In] / [Out]: an array is copied both ways; an 'out' value is not copied in
                // and an 'in' value is not copied back
                builder.AddArgument(new ModuleKernelArgument(ordinal, parameter.Name.ToCSharpIdentifier(), type.Kind,
                    type.ElementTypeName ?? type.TypeName,
                    CopyIn: type.RefKind != RefKind.Out,
                    CopyOut: type.RefKind != RefKind.In,
                    AllowsNull: type.AllowsNull));
            }
        }

        return builder.ToPlan();
    }
}
