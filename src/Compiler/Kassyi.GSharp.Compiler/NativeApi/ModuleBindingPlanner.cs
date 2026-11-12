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
                builder.AddLaunchSetting(type.LaunchSetting, parameter.Name);
            }
            else
            {
                // A YAML binding has no [In] / [Out]: an array is copied both ways
                builder.AddArgument(new ModuleKernelArgument(parameter.Name, type.Kind,
                    type.ElementTypeName ?? type.TypeName, CopyIn: true, CopyOut: true));
            }
        }

        return builder.ToPlan();
    }
}
