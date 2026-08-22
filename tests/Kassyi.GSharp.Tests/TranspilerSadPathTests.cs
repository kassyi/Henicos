using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kassyi.GSharp.Transpiler;
using Xunit;

namespace Kassyi.GSharp.Tests
{
    public class TranspilerSadPathTests
    {
        public static IEnumerable<object[]> GetSadPathKernels()
        {
            var methods = typeof(SadPathKernels).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<GpuKernelAttribute>() != null);
                
            foreach (var method in methods)
            {
                yield return new object[] { method.Name };
            }
        }

        [Theory]
        [MemberData(nameof(GetSadPathKernels))]
        public void Transpile_UnsupportedKernel_ThrowsNotSupportedException(string methodName)
        {
            var md = Mono.Cecil.ModuleDefinition.ReadModule(typeof(SadPathKernels).Assembly.Location);
            var type = md.Types.First(t => t.Name == "SadPathKernels");
            var methodDef = type.Methods.First(m => m.Name == methodName);
            
            var astBuilder = new AstBuilder();
            
            // Should throw NotSupportedException during parsing
            Assert.Throws<NotSupportedException>(() => 
            {
                astBuilder.ParseMethod(methodDef);
            });
        }
    }
}
