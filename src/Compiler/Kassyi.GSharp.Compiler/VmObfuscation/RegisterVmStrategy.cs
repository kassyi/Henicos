namespace Kassyi.GSharp.Compiler.VmObfuscation;

internal class RegisterVmStrategy : IVmStrategy
{
    public void EmitBytecodeGeneration(SourceWriter writer, IMethodSymbol method, byte[] aesKey, Random rng,
        byte[] opcodes)
    {
        var opLdi = opcodes[0];
        var opXor = opcodes[1];
        var opLdm = opcodes[2];
        var opStm = opcodes[3];

        var regMap = Enumerable.Range(0, 16).OrderBy(_ => rng.Next()).ToArray();
        var addrNameHash = 101;
        var bytecode = new List<byte>();

        void emitLdi(int logicalReg, int value)
        {
            bytecode.Add(opLdi);
            bytecode.Add((byte)regMap[logicalReg]);
            bytecode.Add((byte)(value & 0xFF));
            bytecode.Add((byte)((value >> 8) & 0xFF));
            bytecode.Add((byte)((value >> 16) & 0xFF));
            bytecode.Add((byte)((value >> 24) & 0xFF));
        }

        void emitXor(int destLogical, int srcLogical)
        {
            bytecode.Add(opXor);
            bytecode.Add((byte)regMap[destLogical]);
            bytecode.Add((byte)regMap[srcLogical]);
        }

        void emitLdm(int destLogical, int addrLogical)
        {
            bytecode.Add(opLdm);
            bytecode.Add((byte)regMap[destLogical]);
            bytecode.Add((byte)regMap[addrLogical]);
        }

        void emitStm(int addrLogical, int srcLogical)
        {
            bytecode.Add(opStm);
            bytecode.Add((byte)regMap[addrLogical]);
            bytecode.Add((byte)regMap[srcLogical]);
        }

        void emitJunk()
        {
            var numJunk = rng.Next(2, 4);
            for (var j = 0; j < numJunk; j++)
            {
                var junkReg = rng.Next(10, 16);
                emitLdi(junkReg, rng.Next());
                emitXor(junkReg, rng.Next(10, 16));
            }
        }

        for (var i = 0; i < aesKey.Length; i++)
        {
            emitJunk();
            var target = aesKey[i];
            if (rng.Next(2) == 0)
            {
                var rand = (byte)rng.Next(256);
                emitLdi(0, rand);
                emitLdi(1, (byte)(target ^ rand));
                emitXor(0, 1);
            }
            else
            {
                var mtMask = (byte)rng.Next(256);
                var diff = (byte)(target ^ mtMask);
                emitLdi(2, addrNameHash);
                emitLdm(1, 2);
                emitLdi(3, mtMask);
                emitXor(1, 3);
                emitLdi(4, diff ^ (VmObfuscatorEmitter.GetDeterministicHashCode(method.Name) ^ mtMask));
                emitXor(1, 4);
                emitLdi(0, 0);
                emitXor(0, 1);
            }

            emitLdi(15, i);
            emitStm(15, 0);
            emitJunk();
        }

        writer.AppendLine($"byte[] bc = new byte[] {{ {string.Join(", ", bytecode)} }};");
        writer.AppendLine("int[] mem = new int[256];");
        writer.AppendLine($"mem[101] = {VmObfuscatorEmitter.GetDeterministicHashCode(method.Name)};");
        writer.AppendLine("int[] regs = new int[16];");
        writer.AppendLine("int ip = 0;");
    }

    public void EmitVmExecution(SourceWriter writer, IMethodSymbol method, byte[] opcodes)
    {
        writer.AppendLine("while (ip < bc.Length)");
        using (writer.Scope())
        {
            writer.AppendLine("byte op = bc[ip++];");
            writer.AppendLine($"if (op == {opcodes[0]})");
            using (writer.Scope())
            {
                writer.AppendLine("int r = bc[ip++];");
                writer.AppendLine(
                    "regs[r] = bc[ip] | (bc[ip + 1] << 8) | (bc[ip + 2] << 16) | (bc[ip + 3] << 24);");
                writer.AppendLine("ip += 4;");
            }

            writer.AppendLine($"else if (op == {opcodes[1]})");
            using (writer.Scope())
            {
                writer.AppendLine("int rd = bc[ip++]; int rs = bc[ip++];");
                writer.AppendLine("regs[rd] ^= regs[rs];");
            }

            writer.AppendLine($"else if (op == {opcodes[2]})");
            using (writer.Scope())
            {
                writer.AppendLine("int rd = bc[ip++]; int ra = bc[ip++];");
                writer.AppendLine("regs[rd] = mem[regs[ra]];");
            }

            writer.AppendLine($"else if (op == {opcodes[3]})");
            using (writer.Scope())
            {
                writer.AppendLine("int ra = bc[ip++]; int rs = bc[ip++];");
                writer.AppendLine("mem[regs[ra]] = regs[rs];");
            }

            writer.AppendLine($"else if (op == {opcodes[4]})");
            using (writer.Scope())
            {
                writer.AppendLine("ip = bc[ip] | (bc[ip + 1] << 8) | (bc[ip + 2] << 16) | (bc[ip + 3] << 24);");
            }
        }
    }

    public void EmitClearLogic(SourceWriter writer)
    {
        writer.AppendLine("for (int i = 0; i < 16; i++) regs[i] = 0;");
        writer.AppendLine("ip = 0;");
    }
}
