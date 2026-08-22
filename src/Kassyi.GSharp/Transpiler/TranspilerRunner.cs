using System.Reflection;
using System.Collections.Concurrent;
using Mono.Cecil;

namespace Kassyi.GSharp.Transpiler;

public class TranspilerRunner
{
    private static readonly ConcurrentDictionary<string, MethodDefinition> s_methodCache = new();

    public static string GenerateKernelCode(string assemblyPath, string targetMethodName)
    {
        var cacheKey = $"{assemblyPath}::{targetMethodName}";
        if (!s_methodCache.TryGetValue(cacheKey, out var targetMethod))
        {
            var md = ModuleDefinition.ReadModule(assemblyPath);
            targetMethod = md.Types
                .SelectMany(t => t.Methods)
                .FirstOrDefault(m => m.Name == targetMethodName && m.CustomAttributes.Any(a => a.AttributeType.Name == "GpuKernelAttribute"));

            if (targetMethod != null)
            {
                s_methodCache[cacheKey] = targetMethod;
            }
        }

        if (targetMethod == null)
        {
            throw new InvalidOperationException($"Method {targetMethodName} marked with [GpuKernel] not found.");
        }

        var builder = new AstBuilder();
        var ctx = new AstContext(builder);
        var ast = builder.ParseMethod(targetMethod, ctx);

        var generator = new CudaGenerator();
        return generator.Generate(ast, ctx.LocalVariables);
    }

    public static void Run(Assembly targetAssembly)
    {
        Console.WriteLine("=== JIT Transpiler E2E Test ===");
        var method = targetAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .FirstOrDefault(m => m.CustomAttributes.Any(a => a.AttributeType.Name == "GpuKernelAttribute"));

        if (method == null)
        {
            throw new InvalidOperationException("No methods marked with [GpuKernel] found.");
        }

        var cudaCode = GenerateKernelCode(targetAssembly.Location, method.Name);
        Console.WriteLine("=== Generated CUDA Code ===");
        Console.WriteLine(cudaCode);
        Console.WriteLine("===========================");
    }
}
