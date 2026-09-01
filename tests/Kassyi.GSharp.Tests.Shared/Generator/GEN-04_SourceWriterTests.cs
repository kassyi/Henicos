#nullable enable
using Kassyi.GSharp.Compiler;

namespace Kassyi.GSharp.Tests.Generator;

public class GEN_04_SourceWriterTests
{
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("FeatureId", "GEN-04")]
    public void Test_SourceWriter_FluentChaining_BasicTokens()
    {
        using var writer = new SourceWriter();
        writer
            .AppendKeyword("auto")
            .AppendSpace()
            .Append("x")
            .AppendSpace()
            .AppendOperator("=")
            .AppendSpace()
            .Append(123)
            .AppendSemiColonLine();

        string result = writer.ToString().Replace("\r\n", "\n");
        Assert.Equal("auto x = 123;\n", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("FeatureId", "GEN-04")]
    public void Test_SourceWriter_FluentChaining_WithNullsAndConditionals()
    {
        using var writer = new SourceWriter();
        writer
            .Append((string?)null)
            .AppendIf(false, "skip_this")
            .AppendIf(true, "keep_this")
            .AppendSpace()
            .AppendLineIf(false, "skip_newline")
            .AppendLineIf(true, "keep_newline")
            .LineIf(false, "skip_line")
            .LineIf(true, "keep_line");

        string result = writer.ToString().Replace("\r\n", "\n");
        Assert.Equal("keep_this keep_newline\nkeep_line\n", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("FeatureId", "GEN-04")]
    public void Test_SourceWriter_FluentChaining_StructuralTokens()
    {
        using var writer = new SourceWriter();
        writer
            .Append("func")
            .AppendParentheses()
            .AppendSpace()
            .Append("arr")
            .AppendBrackets()
            .AppendSpace()
            .AppendEmptyScope();

        Assert.Equal("func() arr[] {}", writer.ToString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("FeatureId", "GEN-04")]
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
    [Trait("Category", "Unit")]
    [Trait("FeatureId", "GEN-04")]
    public void Test_SourceWriter_FluentChaining_InterpolatedStrings()
    {
        using var writer = new SourceWriter();
        string name = "Kernel";
        int id = 42;

        writer
            .Line($"// Header for {name}")
            .Append($"__global__ void {name}_{id}")
            .AppendParentheses()
            .AppendSpace()
            .AppendEmptyScope();

        string result = writer.ToString().Replace("\r\n", "\n");
        Assert.Equal("// Header for Kernel\n__global__ void Kernel_42() {}", result);
    }
}
