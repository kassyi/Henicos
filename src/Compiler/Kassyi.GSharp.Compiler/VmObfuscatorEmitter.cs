using Kassyi.GSharp.Compiler.VmObfuscation;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Generates Virtualization-Based Obfuscation (VBO) code for AOT Key Derivation.
/// This emitter outputs a minimal Turing-tarpit style virtual machine in C#.
/// </summary>
internal static class VmObfuscatorEmitter
{
    private static readonly IVmStrategy[] s_strategies =
    [
        new NandStackVmStrategy(),
        new RegisterVmStrategy(),
        new AutomatonVmStrategy(),
        new ArithmeticVmStrategy()
    ];

    internal static int GetDeterministicHashCode(string? str)
    {
        if (str == null)
        {
            return 0;
        }

        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in str)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return hash;
        }
    }

    public static void EmitKeyDerivationVm(SourceWriter writer, IMethodSymbol method, byte[] aesKey, int? seed = null)
    {
        var rng = new Random(seed ?? (aesKey.Sum(b => b) + GetDeterministicHashCode(method.Name)));

        // 1. Opcode Shuffling
        var opcodes = new byte[250];
        for (int i = 0; i < 250; i++)
        {
            opcodes[i] = (byte)(i + 1);
        }

        for (int i = opcodes.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (opcodes[i], opcodes[j]) = (opcodes[j], opcodes[i]);
        }

        // 2. Select Strategy
        var strategy = s_strategies[rng.Next(s_strategies.Length)];

        writer.AppendLine(
            "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]");
        writer.AppendLine("private static byte[] DeriveObfuscatedKey()");
        using (writer.Scope())
        {
            strategy.EmitBytecodeGeneration(writer, method, aesKey, rng, opcodes);
            strategy.EmitVmExecution(writer, method, opcodes);

            // 3. Self-Destruction & Common Return
            writer.AppendLine("for (int i = 32; i < 256; i++) mem[i] = 0;");
            strategy.EmitClearLogic(writer);
            writer.AppendLine("System.Array.Clear(bc, 0, bc.Length);");

            writer.AppendLine("byte[] k = new byte[32];");
            writer.AppendLine("for (int i = 0; i < 32; i++) k[i] = (byte)mem[i];");
            writer.AppendLine("return k;");
        }

        writer.AppendLine();
    }
}
