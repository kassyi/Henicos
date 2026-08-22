namespace Kassyi.GSharp.Transpiler;

/// <summary>
/// Debugging utility for dumping Mono.Cecil IL instructions to readable strings.
/// </summary>
public static class IlDumper
{
    private static readonly Dictionary<string, ModuleDefinition> s_modules = new();

    public static string DumpMethod(string assemblyPath, string typeFullName, string methodName)
    {
        if (!s_modules.TryGetValue(assemblyPath, out var module))
        {
            module = ModuleDefinition.ReadModule(assemblyPath);
            s_modules[assemblyPath] = module;
        }

        var type = module.GetType(typeFullName);
        if (type == null)
        {
            throw new ArgumentException($"Type {typeFullName} not found in {assemblyPath}");
        }

        var targetMethod = type.Methods.FirstOrDefault(m => m.Name == methodName && m.HasBody);
        return targetMethod == null
            ? throw new ArgumentException($"Method {methodName} not found or has no body in {typeFullName}")
            : DumpMethod(targetMethod);
    }

    public static string DumpMethod(MethodDefinition method)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// Method: {method.FullName}");

        if (method.Body.Variables.Count > 0)
        {
            sb.AppendLine("// Variables:");
            foreach (var v in method.Body.Variables)
            {
                sb.AppendLine($"//   [{v.Index}] {v.VariableType.FullName}");
            }
        }

        sb.AppendLine();

        foreach (var ins in method.Body.Instructions)
        {
            sb.AppendLine(ins.ToString());
        }

        sb.AppendLine();

        if (method.Body.ExceptionHandlers.Count <= 0)
        {
            return sb.ToString();
        }

        sb.AppendLine("// Exception Handlers:");
        foreach (var eh in method.Body.ExceptionHandlers)
        {
            sb.AppendLine(
                $"//   {eh.HandlerType} : Try [{eh.TryStart.Offset:x4} - {eh.TryEnd.Offset:x4}] Handler [{eh.HandlerStart.Offset:x4} - {eh.HandlerEnd.Offset:x4}]");
        }

        return sb.ToString();
    }
}