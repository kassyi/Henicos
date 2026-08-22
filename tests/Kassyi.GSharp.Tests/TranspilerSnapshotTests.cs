using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Transpiler;
using Mono.Cecil;
using Xunit;
using VerifyXunit;
using System.Threading.Tasks;
using Kassyi.GSharp.Transpiler.Ast;
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests
{
    public class TranspilerSnapshotTests
    {
        private static readonly TypeDefinition s_typeDef;

        static TranspilerSnapshotTests()
        {
            var md = ModuleDefinition.ReadModule(typeof(SnapshotKernels).Assembly.Location);
            s_typeDef = md.Types.First(t => t.Name == "SnapshotKernels");
        }

        private string TranspileMethod(string methodName)
        {
            var methodDef = s_typeDef.Methods.First(m => m.Name == methodName);

            var builder = new AstBuilder();
            var ctx = new AstContext(builder);
            var ast = builder.ParseMethod(methodDef, ctx);

            var generator = new CudaGenerator();
            return generator.Generate(ast, ctx.LocalVariables);
        }

        public static TheoryData<string> GetSnapshotKernels()
        {
            var data = new TheoryData<string>();
            foreach (var method in s_typeDef.Methods)
            {
                if (method.CustomAttributes.Any(a => a.AttributeType.Name == "GpuKernelAttribute"))
                {
                    data.Add(method.Name);
                }
            }
            return data;
        }

        [Theory]
        [MemberData(nameof(GetSnapshotKernels))]
        public Task KernelSnapshot(string kernelName)
        {
            var code = TranspileMethod(kernelName);
            return Verifier.Verify(code)
                .UseDirectory("Snapshots")
                .UseMethodName(kernelName);
        }
    }

    public static class SnapshotKernels
    {
        [GpuKernel]
        public static void EmptyKernel()
        {
        }

        [GpuKernel]
        public static void IfElseTest(int[] data, int threshold)
        {
            if (data[0] > threshold)
            {
                data[1] = 1;
            }
            else
            {
                data[1] = 0;
            }
        }

        [GpuKernel]
        public static void ForLoopTest(int[] data, int length)
        {
            for (int i = 0; i < length; i++)
            {
                data[i] = i * 2;
            }
        }

        [GpuKernel]
        public static void WhileLoopTest(int[] data, int length)
        {
            int i = 0;
            while (i < length)
            {
                data[i] = i * 2;
                i++;
            }
        }

        [GpuKernel]
        public static void ComparisonOperators(int[] result, int a, int b)
        {
            result[0] = a == b ? 1 : 0;
            result[1] = a > b ? 1 : 0;
            result[2] = a < b ? 1 : 0;
        }

        [GpuKernel]
        public static void ArithmeticOperators(int[] result, int a, int b)
        {
            result[0] = a + b;
            result[1] = a - b;
            result[2] = a * b;
            result[3] = a / b;
            result[4] = a % b;
            result[5] = a & b;
            result[6] = a | b;
            result[7] = a ^ b;
            result[8] = a << 5;
            result[9] = a >> 5;
        }

        // --- New Kernels for testing --- //

        [GpuKernel]
        public static void Device_MultiplySquare(int[] result, int val)
        {
            // Dummy intrinsic
        }

        [GpuKernel]
        public static void MethodCallTest(int[] data)
        {
            Device_MultiplySquare(data, data[0]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int InlineHelper(int a, int b)
        {
            int c = a + b;
            return c * 2;
        }

        [GpuKernel]
        public static void InlineExpansionTest(int[] data)
        {
            int x = InlineHelper(data[0], data[1]);
            int y = InlineHelper(data[2], data[3]);
            data[4] = x + y;
        }

        [GpuKernel]
        public static void EarlyReturnTest(int[] data, int flag)
        {
            if (flag == 0)
            {
                data[0] = -1;
                return;
            }
            data[0] = 1;
        }
    }
}
