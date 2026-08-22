namespace Kassyi.GSharp.Transpiler;

public class TranspilerRunner
{
    private static readonly ConcurrentDictionary<string, MethodDefinition> s_methodCache = new();

    public static string GenerateKernelCode(string assemblyPath, string typeFullName, string targetMethodName)
    {
        var metadata = Pipeline.MethodExtractor.Extract(assemblyPath, typeFullName, targetMethodName);
        var astContext = AstBuilder.Build(metadata);
        var transpiledSource = CudaGenerator.Generate(astContext);
        return transpiledSource.CudaCode;
    }

    public static void Run(Assembly targetAssembly)
    {
        Console.WriteLine("=== JIT Transpiler E2E Test ===");
        var method = targetAssembly.GetTypes()
            .SelectMany(t =>
                t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                             BindingFlags.Instance))
            .FirstOrDefault(m => m.CustomAttributes.Any(a => a.AttributeType.Name == "GpuKernelAttribute"));

        if (method == null)
        {
            throw new InvalidOperationException("No methods marked with [GpuKernel] found.");
        }

        var metadata =
            Pipeline.MethodExtractor.Extract(targetAssembly.Location, method.DeclaringType!.FullName!, method.Name);
        var astContext = Ast.AstBuilder.Build(metadata);
        var source = CudaGenerator.Generate(astContext);

        Console.WriteLine("=== Generated CUDA Code ===");
        Console.WriteLine(source.CudaCode);
        Console.WriteLine("===========================");
    }
}
