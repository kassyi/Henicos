using System;
using System.Reflection;
using System.IO;
using Mono.Cecil;

namespace Kassyi.GSharp.Transpiler
{
    public class TranspilerRunner
    {
        public static void Run()
        {
            Console.WriteLine("=== JIT Transpiler E2E Test ===");
            
            // Read the current assembly using Mono.Cecil
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            var md = ModuleDefinition.ReadModule(assemblyPath);
            
            var type = md.GetType("Kassyi.GSharp.DummyMath");
            if (type == null) {
                Console.WriteLine("Type not found.");
                return;
            }
            
            MethodDefinition targetMethod = null;
            foreach (var m in type.Methods)
            {
                if (m.Name == "SimpleLoop")
                {
                    targetMethod = m;
                    break;
                }
            }
            
            if (targetMethod == null) {
                Console.WriteLine("Method not found.");
                return;
            }
            
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
