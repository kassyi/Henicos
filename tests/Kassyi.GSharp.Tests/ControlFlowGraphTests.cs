using Kassyi.GSharp.Transpiler;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Kassyi.GSharp.Tests;

public class ControlFlowGraphTests
{
    private MethodDefinition CreateDummyMethod()
    {
        var module = ModuleDefinition.CreateModule("Dummy", ModuleKind.Dll);
        return new MethodDefinition("Dummy", MethodAttributes.Public, module.TypeSystem.Void);
    }

    [Fact]
    public void Constructor_ShouldBuildBasicBlocks()
    {
        var method = CreateDummyMethod();
        var il = method.Body.GetILProcessor();
        il.Append(Instruction.Create(OpCodes.Nop));
        il.Append(Instruction.Create(OpCodes.Ret));
        var instructions = method.Body.Instructions;
        
        var cfg = ControlFlowGraph.Build(instructions);
        
        Assert.NotNull(cfg.Blocks);
        Assert.Single(cfg.Blocks);
        Assert.Equal(2, cfg.Blocks[0].Instructions.Count);
    }

    [Fact]
    public void Constructor_ShouldSplitBlocksOnLabelsAndJumps()
    {
        var method = CreateDummyMethod();
        var il = method.Body.GetILProcessor();

        var nop1 = Instruction.Create(OpCodes.Nop);
        var nop2 = Instruction.Create(OpCodes.Nop);
        var br = Instruction.Create(OpCodes.Br, nop1);

        il.Append(nop1);
        il.Append(nop2);
        il.Append(br);
        var instructions = method.Body.Instructions;
        
        var cfg = ControlFlowGraph.Build(instructions);
        
        Assert.NotEmpty(cfg.Blocks);
    }

    [Fact]
    public void ConnectBlocks_ShouldCreateEdges()
    {
        var method = CreateDummyMethod();
        var il = method.Body.GetILProcessor();

        var target = Instruction.Create(OpCodes.Ret);
        var branch = Instruction.Create(OpCodes.Brtrue, target);
        var nop1 = Instruction.Create(OpCodes.Nop);
        var nop2 = Instruction.Create(OpCodes.Nop);

        il.Append(nop1);
        il.Append(branch);
        il.Append(nop2);
        il.Append(target);
        
        var instructions = method.Body.Instructions;
        
        var cfg = ControlFlowGraph.Build(instructions);
        
        Assert.True(cfg.Blocks.Count >= 3);
        var blockWithBranch = cfg.Blocks.First(b => b.Instructions.Contains(branch));
        Assert.NotNull(blockWithBranch.Target);
        Assert.NotNull(blockWithBranch.Fallthrough);
    }
}
