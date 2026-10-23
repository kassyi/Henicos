#nullable enable
using System.Threading.Tasks;
using VerifyXunit;

namespace Kassyi.GSharp.Tests.Shared.Generator;

public class SourceWriterTests
{
    [Fact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public void Test_SourceWriter_FluentChaining_BasicTokens()
    {
        using var writer = new SourceWriter();
        writer
            .Append("auto")
            .Append(' ')
            .Append("x")
            .Append(' ')
            .Append("=")
            .Append(' ')
            .Append(123)
            .AppendLine(";");

        string result = writer.ToString().Replace("\r\n", "\n");
        Assert.Equal("auto x = 123;\n", result);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public void Test_SourceWriter_FluentChaining_WithNullsAndConditionals()
    {
        using var writer = new SourceWriter();
        writer
            .Append((string?)null)
            .AppendIf(false, "skip_this")
            .AppendIf(true, "keep_this")
            .Append(' ')
            .AppendLineIf(false, "skip_newline")
            .AppendLineIf(true, "keep_newline")
            .LineIf(false, "skip_line")
            .LineIf(true, "keep_line");

        string result = writer.ToString().Replace("\r\n", "\n");
        Assert.Equal("keep_this keep_newline\nkeep_line\n", result);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public void Test_SourceWriter_FluentChaining_StructuralTokens()
    {
        using var writer = new SourceWriter();
        writer
            .Append("func")
            .Append("()")
            .Append(' ')
            .Append("arr")
            .Append("[]")
            .Append(' ')
            .Append("{}");

        Assert.Equal("func() arr[] {}", writer.ToString());
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public void Test_SourceWriter_FluentChaining_RepeatedNewlines()
    {
        using var writer = new SourceWriter();
        writer
            .Append("line1")
            .AppendLine()
            .AppendLine()
            .Append("line2");

        string result = writer.ToString().Replace("\r\n", "\n");
        Assert.Equal("line1\nline2", result);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public void Test_SourceWriter_FluentChaining_InterpolatedStrings()
    {
        using var writer = new SourceWriter();
        string name = "Kernel";
        int id = 42;

        writer
            .Line($"// Header for {name}")
            .Append($"__global__ void {name}_{id}")
            .Append("()")
            .Append(' ')
            .Append("{}");

        string result = writer.ToString().Replace("\r\n", "\n");
        Assert.Equal("// Header for Kernel\n__global__ void Kernel_42() {}", result);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public Task Test_SourceWriter_NestedScopeAndIndentation_Snapshots()
    {
        using var writer = new SourceWriter();
        writer.Line("void kernel()");
        using (writer.Scope())
        {
            writer.Line("int x = 0;");
            writer.Line("if (true)");
            using (writer.Scope())
            {
                writer.Line("x++;");
            }
        }

        var methodName = nameof(Test_SourceWriter_NestedScopeAndIndentation_Snapshots);
        return Verifier.Verify(writer.ToString(), "cs").UseFastHashSnapshotName(methodName);
    }
}

