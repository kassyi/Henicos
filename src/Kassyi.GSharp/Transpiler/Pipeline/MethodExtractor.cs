namespace Kassyi.GSharp.Transpiler.Pipeline;

/// <summary>
/// Pipeline Step 1: Extracts GpuKernelMetadata from the target assembly.
/// </summary>
public static class MethodExtractor
{
    private static readonly ConcurrentDictionary<string, GpuKernelMetadata> s_cache = new();

    public static GpuKernelMetadata Extract(string assemblyPath, string typeFullName, string methodName)
    {
        var cacheKey = $"{assemblyPath}::{typeFullName}::{methodName}";

        if (s_cache.TryGetValue(cacheKey, out var metadata))
        {
            return metadata;
        }

        var md = ModuleDefinition.ReadModule(assemblyPath);
        var type = md.GetType(typeFullName);
        if (type == null)
        {
            throw new ArgumentException($"Type {typeFullName} not found in {assemblyPath}.");
        }

        var targetMethod = type.Methods.FirstOrDefault(m =>
            m.Name == methodName &&
            (m.CustomAttributes.Any(a => a.AttributeType.Name == "GpuKernelAttribute") || m.HasBody));

        if (targetMethod == null)
        {
            throw new ArgumentException($"Method {methodName} not found or has no body in {typeFullName}.");
        }

        metadata = new GpuKernelMetadata(
            MethodDefinition: targetMethod,
            AssemblyPath: assemblyPath,
            DeclaringTypeFullName: typeFullName,
            MethodName: methodName
        );

        s_cache[cacheKey] = metadata;
        return metadata;
    }
}