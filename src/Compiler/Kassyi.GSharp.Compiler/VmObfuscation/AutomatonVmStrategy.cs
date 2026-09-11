namespace Kassyi.GSharp.Compiler.VmObfuscation;

internal class VmNode
{
    public int Id;
    public byte Opcode;
    public int Arg1;
    public int Arg2;
    public int NextId;
    public int ByteOffset;
}

internal class AutomatonVmStrategy : IVmStrategy
{
    public void EmitBytecodeGeneration(SourceWriter writer, IMethodSymbol method, byte[] aesKey, Random rng,
        byte[] opcodes)
    {
        var opSet = opcodes[0];
        var opXor = opcodes[1];
        var opLmt = opcodes[2];
        var opJnk = opcodes[3];

        var nodes = new List<VmNode>();
        var nextNodeId = 1;

        void addNode(byte op, int a1, int a2)
        {
            var n = new VmNode { Id = nextNodeId++, Opcode = op, Arg1 = a1, Arg2 = a2 };
            nodes.Add(n);
        }

        void addJunk()
        {
            var num = rng.Next(1, 3);
            for (var j = 0; j < num; j++)
            {
                addNode(opJnk, rng.Next(150, 201), rng.Next());
            }
        }

        for (var i = 0; i < aesKey.Length; i++)
        {
            addJunk();
            var target = aesKey[i];
            if (rng.Next(2) == 0)
            {
                var rand = (byte)rng.Next(256);
                addNode(opSet, 50, rand);
                addNode(opSet, 51, (byte)(target ^ rand));
                addNode(opXor, 50, 51);
                addNode(opSet, i, 0);
                addNode(opXor, i, 50);
            }
            else
            {
                var mtMask = (byte)rng.Next(256);
                var diff = (byte)(target ^ mtMask);
                addNode(opLmt, 50, 0);
                addNode(opSet, 51, mtMask);
                addNode(opXor, 50, 51);
                addNode(opSet, 52, diff ^ (VmObfuscatorEmitter.GetDeterministicHashCode(method.Name) ^ mtMask));
                addNode(opXor, 50, 52);
                addNode(opSet, i, 0);
                addNode(opXor, i, 50);
            }

            addJunk();
        }

        for (var i = 0; i < nodes.Count - 1; i++)
        {
            nodes[i].NextId = nodes[i + 1].Id;
        }

        nodes[nodes.Count - 1].NextId = -1;

        var initialId = nodes[0].Id;
        var shuffledNodes = nodes.OrderBy(_ => rng.Next()).ToList();
        for (var i = 0; i < shuffledNodes.Count; i++)
        {
            shuffledNodes[i].ByteOffset = i * 13;
        }

        var idToOffset = shuffledNodes.ToDictionary(n => n.Id, n => n.ByteOffset);
        idToOffset[-1] = -1;

        var bytecode = new List<byte>();
        foreach (var n in shuffledNodes)
        {
            bytecode.Add(n.Opcode);
            bytecode.Add((byte)(n.Arg1 & 0xFF));
            bytecode.Add((byte)((n.Arg1 >> 8) & 0xFF));
            bytecode.Add((byte)((n.Arg1 >> 16) & 0xFF));
            bytecode.Add((byte)((n.Arg1 >> 24) & 0xFF));
            bytecode.Add((byte)(n.Arg2 & 0xFF));
            bytecode.Add((byte)((n.Arg2 >> 8) & 0xFF));
            bytecode.Add((byte)((n.Arg2 >> 16) & 0xFF));
            bytecode.Add((byte)((n.Arg2 >> 24) & 0xFF));
            var nextOffset = idToOffset[n.NextId];
            bytecode.Add((byte)(nextOffset & 0xFF));
            bytecode.Add((byte)((nextOffset >> 8) & 0xFF));
            bytecode.Add((byte)((nextOffset >> 16) & 0xFF));
            bytecode.Add((byte)((nextOffset >> 24) & 0xFF));
        }

        writer.AppendLine($"byte[] bc = new byte[] {{ {string.Join(", ", bytecode)} }};");
        writer.AppendLine("int[] mem = new int[256];");
        writer.AppendLine($"mem[101] = {VmObfuscatorEmitter.GetDeterministicHashCode(method.Name)};");
        writer.AppendLine($"int ip = {idToOffset[initialId]};");
    }

    public void EmitVmExecution(SourceWriter writer, IMethodSymbol method, byte[] opcodes)
    {
        writer.AppendLine("while (ip >= 0 && ip < bc.Length)");
        using (writer.Scope())
        {
            writer.AppendLine("byte op = bc[ip];");
            writer.AppendLine("int a1 = bc[ip+1] | (bc[ip+2]<<8) | (bc[ip+3]<<16) | (bc[ip+4]<<24);");
            writer.AppendLine("int a2 = bc[ip+5] | (bc[ip+6]<<8) | (bc[ip+7]<<16) | (bc[ip+8]<<24);");
            writer.AppendLine("int nextIp = bc[ip+9] | (bc[ip+10]<<8) | (bc[ip+11]<<16) | (bc[ip+12]<<24);");
            writer.AppendLine($"if (op == {opcodes[0]}) mem[a1] = a2;");
            writer.AppendLine($"else if (op == {opcodes[1]}) mem[a1] ^= mem[a2];");
            writer.AppendLine(
                $"else if (op == {opcodes[2]}) mem[a1] = {VmObfuscatorEmitter.GetDeterministicHashCode(method.Name)};");
            writer.AppendLine($"else if (op == {opcodes[3]}) mem[a1] ^= a2;");
            writer.AppendLine("ip = nextIp;");
        }
    }

    public void EmitClearLogic(SourceWriter writer) => writer.AppendLine("ip = 0;");
}
