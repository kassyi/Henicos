using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil.Cil;

namespace Kassyi.GSharp.Transpiler
{
    public class BasicBlock
    {
        public int Id { get; }
        public List<Instruction> Instructions { get; } = new();
        public BasicBlock? Fallthrough { get; set; }
        public BasicBlock? Target { get; set; }
        public HashSet<BasicBlock> Predecessors { get; } = new();

        public BasicBlock(int id)
        {
            Id = id;
        }

        public override string ToString() => $"Block_{Id} (Instrs: {Instructions.Count})";
    }

    public class ControlFlowGraph
    {
        public BasicBlock EntryBlock { get; }
        public IReadOnlyList<BasicBlock> Blocks { get; }

        public ControlFlowGraph(BasicBlock entryBlock, IReadOnlyList<BasicBlock> allBlocks)
        {
            EntryBlock = entryBlock;
            Blocks = allBlocks;
        }

        public static ControlFlowGraph Build(IEnumerable<Instruction> instructions)
        {
            var instructionList = instructions.ToList();
            if (instructionList.Count == 0)
            {
                var empty = new BasicBlock(0);
                return new ControlFlowGraph(empty, new[] { empty });
            }

            var leaders = new HashSet<Instruction>();
            leaders.Add(instructionList[0]);

            // Pass 1: Identify leaders (targets of branches and instructions following branches)
            foreach (var ins in instructionList)
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
                            leaders.Add(t);
                    }

                    if (ins.Next != null)
                    {
                        leaders.Add(ins.Next);
                    }
                }
            }

            // Pass 2: Create blocks
            var blocksByLeader = new Dictionary<Instruction, BasicBlock>();
            var allBlocks = new List<BasicBlock>();
            BasicBlock? currentBlock = null;
            int blockId = 0;

            foreach (var ins in instructionList)
            {
                if (leaders.Contains(ins))
                {
                    currentBlock = new BasicBlock(blockId++);
                    blocksByLeader[ins] = currentBlock;
                    allBlocks.Add(currentBlock);
                }

                currentBlock!.Instructions.Add(ins);
            }

            // Pass 3: Connect edges
            for (int i = 0; i < allBlocks.Count; i++)
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

                    // Conditional branches fall through to the next block
                    if (!IsUnconditionalBranch(lastIns.OpCode.Code) && lastIns.Next != null && blocksByLeader.TryGetValue(lastIns.Next, out var nextBlock))
                    {
                        block.Fallthrough = nextBlock;
                        nextBlock.Predecessors.Add(block);
                    }
                }
                else if (lastIns.OpCode.Code != Code.Ret && lastIns.OpCode.Code != Code.Throw)
                {
                    // Sequential fallthrough
                    if (i + 1 < allBlocks.Count)
                    {
                        var nextBlock = allBlocks[i + 1];
                        block.Fallthrough = nextBlock;
                        nextBlock.Predecessors.Add(block);
                    }
                }
            }

            return new ControlFlowGraph(blocksByLeader[instructionList[0]], allBlocks);
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
                Code.Leave or Code.Leave_S => true,
                _ => false
            };
        }

        private static bool IsUnconditionalBranch(Code code)
        {
            return code is Code.Br or Code.Br_S or Code.Leave or Code.Leave_S;
        }
    }
}
