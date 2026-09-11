namespace Kassyi.GSharp.Compiler.VmObfuscation;

internal class ArithmeticVmStrategy : IVmStrategy
{
    public void EmitBytecodeGeneration(SourceWriter writer, IMethodSymbol method, byte[] aesKey, Random rng,
        byte[] opcodes)
    {
        var opLdc = opcodes[0];
        var opAdd = opcodes[1];
        var opMul = opcodes[2];
        var opMod = opcodes[3];
        var opLmt = opcodes[4];
        var opStm = opcodes[5];

        var bytecode = new List<byte>();

        void emit(byte op, int val)
        {
            bytecode.Add(op);
            bytecode.Add((byte)(val & 0xFF));
            bytecode.Add((byte)((val >> 8) & 0xFF));
            bytecode.Add((byte)((val >> 16) & 0xFF));
            bytecode.Add((byte)((val >> 24) & 0xFF));
        }

        void emitJunk()
        {
            var num = rng.Next(1, 4);
            for (var j = 0; j < num; j++)
            {
                var type = rng.Next(3);
                switch (type)
                {
                    case 0:
                        emit(opAdd, rng.Next());

                        emit(opAdd, -bytecode[bytecode.Count - 4]);
                        break;
                    case 1:
                        emit(opMul, 1);
                        break;
                    default:
                        emit(opLdc, rng.Next());
                        emit(opMul, rng.Next());
                        break;
                }
            }
        }

        for (var i = 0; i < aesKey.Length; i++)
        {
            emitJunk();
            var target = aesKey[i];
            if (rng.Next(2) == 0)
            {
                var r = rng.Next(1, 1000);
                var m = rng.Next(1, 100);
                var a = target - ((r * m) % 256);
                if (a < 0)
                {
                    a += 256;
                }

                emit(opLdc, r);
                emit(opMul, m);
                emit(opAdd, a);
                emit(opMod, 256);
            }
            else
            {
                var nameHash = VmObfuscatorEmitter.GetDeterministicHashCode(method.Name);
                var r = rng.Next(1, 1000);
                var a = target - ((r + nameHash) % 256);
                if (a < 0)
                {
                    a += 256;
                }

                emit(opLdc, r);
                emit(opLmt, 0);
                emit(opAdd, a);
                emit(opMod, 256);
            }

            emit(opStm, i);
        }

        writer.AppendLine($"byte[] bc = new byte[] {{ {string.Join(", ", bytecode)} }};");
        writer.AppendLine("int[] mem = new int[256];");
        writer.AppendLine("int acc = 0;");
        writer.AppendLine("int ip = 0;");
    }

    public void EmitVmExecution(SourceWriter writer, IMethodSymbol method, byte[] opcodes)
    {
        writer.AppendLine("while (ip < bc.Length)");
        using (writer.Scope())
        {
            writer.AppendLine("byte op = bc[ip++];");
            writer.AppendLine("int val = bc[ip++] | (bc[ip++]<<8) | (bc[ip++]<<16) | (bc[ip++]<<24);");
            writer.AppendLine("unchecked {");
            writer.AppendLine($"if (op == {opcodes[0]}) acc = val;");
            writer.AppendLine($"else if (op == {opcodes[1]}) acc += val;");
            writer.AppendLine($"else if (op == {opcodes[2]}) acc *= val;");
            writer.AppendLine($"else if (op == {opcodes[3]}) acc %= (val == 0 ? 1 : val);");
            writer.AppendLine($"else if (op == {opcodes[4]}) acc += {VmObfuscatorEmitter.GetDeterministicHashCode(method.Name)};");
            writer.AppendLine($"else if (op == {opcodes[5]}) mem[val] = acc & 0xFF;");
            writer.AppendLine("}");
        }
    }

    public void EmitClearLogic(SourceWriter writer) => writer.AppendLine("acc = 0; ip = 0;");
}
