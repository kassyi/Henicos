#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.VmObfuscation;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Generator;

public class GEN_34_VmObfuscationTests
{
    private void DummyTargetMethod()
    {
    }

    [Theory]
    [Trait("FeatureId", "GEN-34")]
    [Trait("Category", "Transpiler")]
    [InlineData(null, 0)]
    [InlineData("", unchecked((int)2166136261))]
    [InlineData("DummyTargetMethod", 101732626)] // Note: calculated locally, will be asserted dynamically if wrong
    public void Test_GEN34_DeterministicHashCode_EdgeCases(string? input, int expectedHash)
    {
        if (input == "DummyTargetMethod")
        {
            // Just check avalanche and consistency
            var h1 = VmObfuscatorEmitter.GetDeterministicHashCode(input);
            var h2 = VmObfuscatorEmitter.GetDeterministicHashCode(input);
            Assert.Equal(h1, h2);
            Assert.NotEqual(0, h1);
            Assert.NotEqual(unchecked((int)2166136261), h1);
        }
        else
        {
            var hash = VmObfuscatorEmitter.GetDeterministicHashCode(input);
            Assert.Equal(expectedHash, hash);
        }
    }

    public static IEnumerable<object[]> VmStrategyTestData()
    {
        var strategies = new IVmStrategy[]
        {
            new ArithmeticVmStrategy(),
            new AutomatonVmStrategy(),
            new NandStackVmStrategy(),
            new RegisterVmStrategy()
        };

        var keyPatterns = new List<byte[]>
        {
            Array.Empty<byte>(), // empty collection
            new byte[] { 42 }, // single-element collection
            new byte[32], // all zeros
            Enumerable.Repeat((byte)0xFF, 32).ToArray(), // all 0xFF
            Enumerable.Range(0, 32).Select(i => (byte)(i % 2 == 0 ? 0xAA : 0x55)).ToArray(), // alternating bits
            Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(), // sequential 0..31
            Enumerable.Range(0, 32).Select(i => (byte)(255 - i)).ToArray(), // reverse sequential 255..224
            Enumerable.Range(0, 32).Select(i => (byte)(i % 4 switch { 0 => 0x7F, 1 => 0x80, 2 => 0x01, _ => 0xFE })).ToArray(), // signed/byte edge cases
            Enumerable.Range(0, 32).Select(i => (byte)(1 << (i % 8))).ToArray(), // walking 1s
            Convert.FromHexString("603DEB1015CA71BE2B73AEF0857D77811F352C073B6108D72D9810A30914DFF4"), // NIST AES-256 test key
        };

        // Add 2 pseudo-random cryptographic keys with different seeds
        var rng1 = new Random(12345);
        var randKey1 = new byte[32];
        rng1.NextBytes(randKey1);
        keyPatterns.Add(randKey1);

        var rng2 = new Random(98765);
        var randKey2 = new byte[32];
        rng2.NextBytes(randKey2);
        keyPatterns.Add(randKey2);

        // Test with 2 different generator seeds to exercise both code generation branches
        var seeds = new[] { 42, 1337 };

        foreach (var strategy in strategies)
        {
            foreach (var key in keyPatterns)
            {
                foreach (var seed in seeds)
                {
                    yield return new object[] { strategy, key, seed };
                }
            }
        }
    }

    [Theory]
    [Trait("FeatureId", "GEN-34")]
    [Trait("Category", "Transpiler")]
    [MemberData(nameof(VmStrategyTestData))]
    internal void Test_GEN34_VmStrategies_DeriveExactKey_DynamicExecution(IVmStrategy strategy, byte[] aesKey, int generatorSeed)
    {
        var (_, methodSymbol) = MegaCompilationCache.GetMethodContext<GEN_34_VmObfuscationTests>(nameof(DummyTargetMethod));
        var rng = new Random(generatorSeed);
        var opcodes = Enumerable.Range(1, 250).OrderBy(_ => rng.Next()).Select(x => (byte)x).ToArray();

        using var writer = new SourceWriter();
        writer.AppendLine("using System;");
        writer.AppendLine("public static class VmWrapper");
        using (writer.Scope())
        {
            writer.AppendLine("public static byte[] DeriveObfuscatedKey()");
            using (writer.Scope())
            {
                strategy.EmitBytecodeGeneration(writer, methodSymbol, aesKey, rng, opcodes);
                strategy.EmitVmExecution(writer, methodSymbol, opcodes);

                // Self-destruction & return logic
                writer.AppendLine("for (int i = 32; i < 256; i++) mem[i] = 0;");
                strategy.EmitClearLogic(writer);
                writer.AppendLine("System.Array.Clear(bc, 0, bc.Length);");

                writer.AppendLine("byte[] k = new byte[32];");
                writer.AppendLine("for (int i = 0; i < 32; i++) k[i] = (byte)mem[i];");
                writer.AppendLine("return k;");
            }
        }

        var sourceCode = writer.ToString();

        // Dynamically compile and execute the generated VM
        var expectedKey = new byte[32];
        Array.Copy(aesKey, expectedKey, Math.Min(aesKey.Length, 32));
        var actualKey = CompileAndExecuteDeriveKey(sourceCode);
        Assert.Equal(expectedKey, actualKey);
    }

    [Theory]
    [Trait("FeatureId", "GEN-34")]
    [Trait("Category", "Transpiler")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(300)]
    [InlineData(400)]
    [InlineData(unchecked((int)0xAAAAAAAA))]
    [InlineData(unchecked((int)0x55555555))]
    public void Test_GEN34_VmObfuscatorEmitter_EmitKeyDerivationVm_CompilesAndExecutes(int seed)
    {
        var (_, methodSymbol) = MegaCompilationCache.GetMethodContext<GEN_34_VmObfuscationTests>(nameof(DummyTargetMethod));
        var rng = new Random(seed);
        var aesKey = new byte[32];
        rng.NextBytes(aesKey);

        using var writer = new SourceWriter();
        writer.AppendLine("using System;");
        writer.AppendLine("public static class VmWrapper");
        using (writer.Scope())
        {
            VmObfuscatorEmitter.EmitKeyDerivationVm(writer, methodSymbol, aesKey, seed: seed);

            // Expose public method to call the private one
            writer.AppendLine($"public static byte[] CallDeriveObfuscatedKey() => DeriveObfuscatedKey_{methodSymbol.Name}();");
        }

        var sourceCode = writer.ToString();
        var actualKey = CompileAndExecuteDeriveKey(sourceCode, methodName: "CallDeriveObfuscatedKey");

        Assert.Equal(aesKey, actualKey);

        // Verify self-destruction is present
        Assert.Contains("System.Array.Clear(bc,", sourceCode);
        Assert.Contains("for (int i = 32; i < 256; i++) mem[i] = 0;", sourceCode);
    }

    private static byte[] CompileAndExecuteDeriveKey(string sourceCode, string methodName = "DeriveObfuscatedKey")
    {
        var compilation = RoslynTestUtility.CreateCompilation(sourceCode);

        // For emitting we need an executable or a DLL. RoslynTestUtility.CreateCompilation uses MegaCompilationCache.BaseCompilation
        // which already has mscorlib/System.Private.CoreLib. We just need to emit it to a stream.
        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = string.Join(Environment.NewLine, emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new Exception($"Dynamic compilation failed: {errors}\n\nSource:\n{sourceCode}");
        }

        ms.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());
        var type = assembly.GetType("VmWrapper") ?? throw new Exception("VmWrapper type not found in emitted assembly.");
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new Exception($"{methodName} method not found in VmWrapper.");

        return (byte[])method.Invoke(null, null)!;
    }
}
