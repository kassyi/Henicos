using System;
using System.Reflection;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Kassyi.GSharp.Transpiler
{
    public class TranspilerRunner
    {
        public static void Run(Assembly targetAssembly)
        {
            Console.WriteLine("=== JIT Transpiler E2E Test ===");
            
            // Read the specified assembly using Mono.Cecil
            string assemblyPath = targetAssembly.Location;
            var md = ModuleDefinition.ReadModule(assemblyPath);
            
            var targetMethods = new System.Collections.Generic.List<MethodDefinition>();
            foreach (var type in md.Types)
            {
                foreach (var m in type.Methods)
                {
                    if (m.CustomAttributes.Any(a => a.AttributeType.Name == "GpuKernelAttribute"))
                    {
                        targetMethods.Add(m);
                    }
                }
            }
            
            if (targetMethods.Count == 0) {
                throw new InvalidOperationException("No methods marked with [GpuKernel] found.");
            }
            
            var targetMethod = targetMethods[0];
            
            Console.WriteLine($"Found method: {targetMethod.FullName}");
            
            // 1. Build AST from IL
            var builder = new AstBuilder();
            var ast = builder.ParseMethod(targetMethod);
            
            // 2. Generate CUDA C++
            var generator = new CudaGenerator();
            string cudaCode = generator.Generate(ast);
            
            Console.WriteLine("=== Generated CUDA Code ===");
            Console.WriteLine(cudaCode);
            Console.WriteLine("===========================");
        }
    }
}
