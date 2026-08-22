using Kassyi.GSharp.Transpiler.Ast;
using Xunit;
using System.Linq;

namespace Kassyi.GSharp.Tests;

public class AstRenamerVisitorTests
{
    private AstRenamerVisitor CreateRenamer(string prefix)
    {
        var builder = new AstBuilder();
        var context = new AstContext(builder);
        return new AstRenamerVisitor(prefix, "dummy_end", null, context, null);
    }

    [Fact]
    public void Visit_VariableNode_ShouldRename()
    {
        var renamer = CreateRenamer("prefix_");
        var varNode = new VariableNode("x");
        
        renamer.Visit(varNode);
        var result = renamer.TransformedStatements.FirstOrDefault() as VariableNode;
        
        Assert.NotNull(result);
        Assert.Equal("prefix_x", result.Name);
    }

    [Fact]
    public void Visit_BlockNode_ShouldRenameChildren()
    {
        var renamer = CreateRenamer("prefix_");
        var block = new BlockNode([
            new VariableNode("x"),
            new VariableNode("y")
        ]);
        
        renamer.Visit(block);
        
        Assert.Equal(2, renamer.TransformedStatements.Count);
        Assert.Equal("prefix_x", ((VariableNode)renamer.TransformedStatements[0]).Name);
        Assert.Equal("prefix_y", ((VariableNode)renamer.TransformedStatements[1]).Name);
    }

    [Fact]
    public void Visit_IfNode_ShouldRenameConditionAndBody()
    {
        var renamer = CreateRenamer("prefix_");
        var ifNode = new IfNode(
            new VariableNode("cond"),
            new BlockNode([new VariableNode("a")]),
            new BlockNode([new VariableNode("b")])
        );

        renamer.Visit(ifNode);
        var result = renamer.TransformedStatements.FirstOrDefault() as IfNode;

        Assert.NotNull(result);
        Assert.Equal("prefix_cond", ((VariableNode)result.Condition).Name);
        Assert.Equal("prefix_a", ((VariableNode)((BlockNode)result.TrueBlock).Statements[0]).Name);
        Assert.Equal("prefix_b", ((VariableNode)((BlockNode)result.FalseBlock!).Statements[0]).Name);
    }

    [Fact]
    public void Visit_ForLoopNode_ShouldRenameAllParts()
    {
        var renamer = CreateRenamer("prefix_");
        var forNode = new ForLoopNode(
            "i",
            new VariableNode("start"),
            new VariableNode("end"),
            new BlockNode([new VariableNode("body")])
        );

        renamer.Visit(forNode);
        var result = renamer.TransformedStatements.FirstOrDefault() as ForLoopNode;

        Assert.NotNull(result);
        Assert.Equal("prefix_i", result.IteratorName);
        Assert.Equal("prefix_start", ((VariableNode)result.Start).Name);
        Assert.Equal("prefix_end", ((VariableNode)result.End).Name);
        Assert.Equal("prefix_body", ((VariableNode)((BlockNode)result.Body).Statements[0]).Name);
    }

    [Fact]
    public void Visit_WhileNode_ShouldRenameConditionAndBody()
    {
        var renamer = CreateRenamer("prefix_");
        var whileNode = new WhileNode(
            new VariableNode("cond"),
            new BlockNode([new VariableNode("body")])
        );

        renamer.Visit(whileNode);
        var result = renamer.TransformedStatements.FirstOrDefault() as WhileNode;

        Assert.NotNull(result);
        Assert.Equal("prefix_cond", ((VariableNode)result.Condition).Name);
        Assert.Equal("prefix_body", ((VariableNode)((BlockNode)result.Body).Statements[0]).Name);
    }
}
