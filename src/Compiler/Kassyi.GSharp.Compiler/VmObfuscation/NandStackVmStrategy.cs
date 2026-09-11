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

        void emitPush(int value)
        {
            bytecode.Add(opPush);
            bytecode.Add((byte)value);
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

        for (var i = 0; i < aesKey.Length; i++)
        {
            emitJunk();
            var target = aesKey[i];
            if (rng.Next(2) == 0)
            {
                var rand = (byte)rng.Next(256);
                var randXor = (byte)(target ^ rand);
                emitPush(rand);
                emitPush(randXor);
                emitPush(randXor);
                emitNand();
                emitPush(rand);
                emitNand();
                emitNand();
            }
            else
            {
                var mtMask = (byte)rng.Next(256);
                var diff = (byte)(target ^ mtMask);
                emitPush(100);
                emitLoad();
                emitPush(mtMask);
                emitPush(mtMask);
                emitNand();
                emitPush(100);
                emitLoad();
                emitNand();
                emitNand();
                emitPush(diff ^ (VmObfuscatorEmitter.GetDeterministicHashCode(method.Name) ^ mtMask));
                emitPush(diff ^ (VmObfuscatorEmitter.GetDeterministicHashCode(method.Name) ^ mtMask));
                emitNand();
                emitPush(0);
                emitLoad();
                emitNand();
                emitNand();
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
        writer.AppendLine("int[] stack = new int[256];");
        writer.AppendLine("int sp = 0;");
        writer.AppendLine("int ip = 0;");
        writer.AppendLine($"mem[100] = {VmObfuscatorEmitter.GetDeterministicHashCode(method.Name)};");
    }

    public void EmitVmExecution(SourceWriter writer, IMethodSymbol method, byte[] opcodes)
    {
        writer.AppendLine("while (ip < bc.Length)");
        using (writer.Scope())
        {
            writer.AppendLine("byte op = bc[ip++];");
            writer.AppendLine($"if (op == {opcodes[0]}) stack[sp++] = bc[ip++];");
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
