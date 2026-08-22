using Mono.Cecil.Cil;

namespace Kassyi.GSharp.Transpiler;

public class BasicBlock
{
    public int Id
    {
        get;
    }
    public List<Instruction> Instructions { get; } = new();
    public BasicBlock? Fallthrough
    {
        get; set;
    }
    public BasicBlock? Target
    {
        get; set;
    }
    public HashSet<BasicBlock> Predecessors { get; } = new();

    public BasicBlock(int id)
    {
        Id = id;
    }

    public override string ToString() => $"Block_{Id} (Instrs: {Instructions.Count})";
}

public class ControlFlowGraph
{
    public BasicBlock EntryBlock
    {
        get;
    }
    public IReadOnlyList<BasicBlock> Blocks
    {
        get;
    }

    public ControlFlowGraph(BasicBlock entryBlock, IReadOnlyList<BasicBlock> allBlocks)
    {
        EntryBlock = entryBlock;
        Blocks = allBlocks;
    }

    public static ControlFlowGraph Build(IEnumerable<Instruction> instructions)
    {
        var instructionList = instructions as IReadOnlyList<Instruction> ?? instructions.ToList();
        if (instructionList.Count == 0)
        {
            var empty = new BasicBlock(0);
            return new ControlFlowGraph(empty, new[] { empty });
        }

        var leaders = IdentifyLeaders(instructionList);
        var (blocksByLeader, allBlocks) = CreateBlocks(instructionList, leaders);
        ConnectEdges(allBlocks, blocksByLeader);

        return new ControlFlowGraph(allBlocks[0], allBlocks);
    }

    private static HashSet<Instruction> IdentifyLeaders(IReadOnlyList<Instruction> instructions)
    {
        var leaders = new HashSet<Instruction>(instructions.Count / 4) { instructions[0] };
        foreach (var ins in instructions)
        {
            if (IsBranch(ins.OpCode.Code))
            {
                if (ins.Operand is Instruction target)
                {
                    leaders.Add(target);
                }
                else if (ins.Operand is Instruction[] targets)
                {
                    foreach (var t in targets)
                    {
                        leaders.Add(t);
                    }
                }
                if (ins.Next != null)
                {
                    leaders.Add(ins.Next);
                }
            }
        }
        return leaders;
    }

    private static (Dictionary<Instruction, BasicBlock>, List<BasicBlock>) CreateBlocks(IReadOnlyList<Instruction> instructions, HashSet<Instruction> leaders)
    {
        var blocksByLeader = new Dictionary<Instruction, BasicBlock>();
        var allBlocks = new List<BasicBlock>();
        BasicBlock? currentBlock = null;
        var blockId = 0;

        foreach (var ins in instructions)
        {
            if (leaders.Contains(ins))
            {
                currentBlock = new BasicBlock(blockId++);
                blocksByLeader[ins] = currentBlock;
                allBlocks.Add(currentBlock);
            }
            currentBlock!.Instructions.Add(ins);
        }
        return (blocksByLeader, allBlocks);
    }

    private static void ConnectEdges(List<BasicBlock> allBlocks, Dictionary<Instruction, BasicBlock> blocksByLeader)
    {
        for (var i = 0; i < allBlocks.Count; i++)
        {
            var block = allBlocks[i];
            var lastIns = block.Instructions.Last();

            if (IsBranch(lastIns.OpCode.Code))
            {
                if (lastIns.Operand is Instruction targetInst && blocksByLeader.TryGetValue(targetInst, out var targetBlock))
                {
                    block.Target = targetBlock;
                    targetBlock.Predecessors.Add(block);
                }
                else if (lastIns.Operand is Instruction[] targets)
                {
                    foreach (var t in targets)
                    {
                        if (blocksByLeader.TryGetValue(t, out var swTarget))
                        {
                            block.Target = swTarget;
                            swTarget.Predecessors.Add(block);
                        }
                    }
                }

                if (lastIns.OpCode.Code != Code.Br && lastIns.OpCode.Code != Code.Br_S &&
                    lastIns.OpCode.Code != Code.Leave && lastIns.OpCode.Code != Code.Leave_S &&
                    i + 1 < allBlocks.Count)
                {
                    block.Fallthrough = allBlocks[i + 1];
                    allBlocks[i + 1].Predecessors.Add(block);
                }
            }
            else if (lastIns.OpCode.Code != Code.Ret && i + 1 < allBlocks.Count)
            {
                block.Fallthrough = allBlocks[i + 1];
                allBlocks[i + 1].Predecessors.Add(block);
            }
        }
    }

    private static bool IsBranch(Code code)
    {
        return code switch
        {
            Code.Br or Code.Br_S or
            Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S or
            Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S or
            Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S or
            Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S or
            Code.Beq or Code.Beq_S or
            Code.Bne_Un or Code.Bne_Un_S or
            Code.Brtrue or Code.Brtrue_S or
            Code.Brfalse or Code.Brfalse_S or
            Code.Leave or Code.Leave_S or
            Code.Switch => true,
            _ => false
        };
    }

    private static bool IsUnconditionalBranch(Code code) => code is Code.Br or Code.Br_S or Code.Leave or Code.Leave_S;
}
