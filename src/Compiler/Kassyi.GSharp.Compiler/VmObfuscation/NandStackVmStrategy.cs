namespace Kassyi.GSharp.Compiler.VmObfuscation;

internal class NandStackVmStrategy : IVmStrategy
{
    public void EmitBytecodeGeneration(SourceWriter writer, IMethodSymbol method, byte[] aesKey, Random rng,
        byte[] opcodes)
    {
        var opPush = opcodes[0];
        var opLoad = opcodes[1];
        var opStore = opcodes[2];
        var opNand = opcodes[3];

        var bytecode = new List<byte>();
        var memMap = Enumerable.Range(0, 32).Concat(Enumerable.Range(32, 224).OrderBy(_ => rng.Next())).ToArray();

        for (var i = 0; i < aesKey.Length; i++)
        {
            var target = aesKey[i];
            var memAddrHash = memMap[100];
            var memAddrTempC = memMap[200];
            var memAddrTempD = memMap[201];
            var memAddrTempE = memMap[202];

            if (rng.Next(2) == 0)
            {
                var rand = (byte)rng.Next(256);
                var randXor = (byte)(target ^ rand);

                // We want to compute: target = rand ^ randXor using NAND.
                // A = rand, B = randXor
                // C = ~(A & B)
                emitPush(rand);
                emitPush(randXor);
                emitNand();
                emitPush(memAddrTempC);
                emitStore(); // mem[200] = C

                // D = ~(A & C)
                emitPush(rand);
                emitPush(memAddrTempC);
                emitLoad();
                emitNand();
                emitPush(memAddrTempD);
                emitStore(); // mem[201] = D

                // E = ~(B & C)
                emitPush(randXor);
                emitPush(memAddrTempC);
                emitLoad();
                emitNand();
                emitPush(memAddrTempE);
                emitStore(); // mem[202] = E

                // result = ~(D & E)
                emitPush(memAddrTempD);
                emitLoad();
                emitPush(memAddrTempE);
                emitLoad();
                emitNand(); // stack top is now target
            }
            else
            {
                var mtMask = (byte)rng.Next(256);
                var diff = (byte)(target ^ mtMask);

                var hash = VmObfuscatorEmitter.GetDeterministicHashCode(method.Name);
                var precomputed = diff ^ (hash ^ mtMask);

                // We just need to compute: target = hash ^ precomputed
                // A = hash (from mem[100]), B = precomputed
                emitPush(memAddrHash);
                emitLoad();
                emitPush(precomputed);
                emitNand();
                emitPush(memAddrTempC);
                emitStore(); // C = ~(A & B)

                emitPush(memAddrHash);
                emitLoad();
                emitPush(memAddrTempC);
                emitLoad();
                emitNand();
                emitPush(memAddrTempD);
                emitStore(); // D = ~(A & C)

                emitPush(precomputed);
                emitPush(memAddrTempC);
                emitLoad();
                emitNand();
                emitPush(memAddrTempE);
                emitStore(); // E = ~(B & C)

                emitPush(memAddrTempD);
                emitLoad();
                emitPush(memAddrTempE);
                emitLoad();
                emitNand(); // stack top is target
            }

            emitPush(i);
            emitStore();
            emitJunk();
        }

        for (var i = 0; i < 5; i++)
        {
            emitPush(0);
            emitPush(1);
            emitNand();
        }

        writer.AppendLine($"byte[] bc = new byte[] {{ {string.Join(", ", bytecode)} }};");
        writer.AppendLine("int[] mem = new int[256];");
        writer.AppendLine($"mem[{memMap[100]}] = {VmObfuscatorEmitter.GetDeterministicHashCode(method.Name)};");
        writer.AppendLine("int[] stack = new int[256];");
        writer.AppendLine("int sp = 0;");
        writer.AppendLine("int ip = 0;");
        return;

        void emitPush(int value)
        {
            bytecode.Add(opPush);
            bytecode.Add((byte)(value & 0xFF));
            bytecode.Add((byte)((value >> 8) & 0xFF));
            bytecode.Add((byte)((value >> 16) & 0xFF));
            bytecode.Add((byte)((value >> 24) & 0xFF));
        }

        void emitLoad() => bytecode.Add(opLoad);

        void emitStore() => bytecode.Add(opStore);

        void emitNand() => bytecode.Add(opNand);

        void emitJunk()
        {
            var numJunk = rng.Next(2, 5);
            for (var j = 0; j < numJunk; j++)
            {
                var junkAddr = rng.Next(150, 201);
                emitPush(rng.Next(256));
                emitPush(rng.Next(256));
                emitNand();
                emitPush(junkAddr);
                emitStore();
            }
        }
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
                writer.AppendLine(
                    "stack[sp++] = bc[ip] | (bc[ip + 1] << 8) | (bc[ip + 2] << 16) | (bc[ip + 3] << 24);");
                writer.AppendLine("ip += 4;");
            }

            writer.AppendLine($"else if (op == {opcodes[1]})");
            using (writer.Scope())
            {
                writer.AppendLine("int addr = stack[--sp];");
                writer.AppendLine("stack[sp++] = mem[addr];");
            }

            writer.AppendLine($"else if (op == {opcodes[2]})");
            using (writer.Scope())
            {
                writer.AppendLine("int addr = stack[--sp];");
                writer.AppendLine("int val = stack[--sp];");
                writer.AppendLine("mem[addr] = val;");
            }

            writer.AppendLine($"else if (op == {opcodes[3]})");
            using (writer.Scope())
            {
                writer.AppendLine("int b = stack[--sp];");
                writer.AppendLine("int a = stack[--sp];");
                writer.AppendLine("stack[sp++] = ~(a & b);");
            }

            writer.AppendLine($"else if (op == {opcodes[4]})");
            using (writer.Scope())
            {
                writer.AppendLine("int target = stack[--sp];");
                writer.AppendLine("int cond = stack[--sp];");
                writer.AppendLine("if (cond == 0) ip = target;");
            }
        }
    }

    public void EmitClearLogic(SourceWriter writer)
    {
        writer.AppendLine("for (int i = 0; i < 256; i++) stack[i] = 0;");
        writer.AppendLine("sp = 0; ip = 0;");
    }
}
