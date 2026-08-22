using System;
using System.IO;
using System.Text;
using Kassyi.GSharp.Transpiler;
using Xunit;

namespace Kassyi.GSharp.Tests
{
    public class TranspilerRunnerTests
    {
        [Fact]
        public void Run_ShouldExecuteWithoutCrashing()
        {
            // Redirect console output to avoid spamming test results
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // This shouldn't throw InvalidOperationException since DummyMath has [GpuKernel]
                TranspilerRunner.Run(typeof(DummyMath).Assembly);
                
                var output = sw.ToString();
                originalOut.WriteLine("--- TEST OUTPUT ---");
                originalOut.WriteLine(output);
                originalOut.WriteLine("-------------------");
                Assert.Contains("=== JIT Transpiler E2E Test ===", output);
                Assert.Contains("=== Generated CUDA Code ===", output);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
