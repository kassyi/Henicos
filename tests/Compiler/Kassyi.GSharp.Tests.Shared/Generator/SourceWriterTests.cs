#nullable enable
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VerifyXunit;

namespace Kassyi.GSharp.Tests.Shared.Generator;

public class SourceWriterTests
{
    [Fact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public void SourceWriter_BasicTokens_AppendsTokensInFluentChain()
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

        Assert.Equal("auto x = 123;" + Environment.NewLine, writer.ToString());
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public void SourceWriter_ConditionalsAndNulls_AppendsOnlyMatchingLines()
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

        Assert.Equal($"keep_this keep_newline{Environment.NewLine}keep_line{Environment.NewLine}", writer.ToString());
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public void SourceWriter_StructuralTokens_AppendsBracketsAndParentheses()
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
    public void SourceWriter_RepeatedNewlines_NormalizesConsecutiveNewlines()
    {
        using var writer = new SourceWriter();
        writer
            .Append("line1")
            .AppendLine()
            .AppendLine()
            .Append("line2");

        Assert.Equal($"line1{Environment.NewLine}line2", writer.ToString());
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public void SourceWriter_InterpolatedStrings_FormatsInterpolatedExpressions()
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

        Assert.Equal($"// Header for Kernel{Environment.NewLine}__global__ void Kernel_42() {{}}", writer.ToString());
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public Task SourceWriter_NestedScopeAndIndentation_MatchesExpectedSnapshot()
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

        var methodName = nameof(SourceWriter_NestedScopeAndIndentation_MatchesExpectedSnapshot);
        return Verifier.Verify(writer.ToString(), "cs").UseFastHashSnapshotName(methodName);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Dispose_BuilderGrewPastThePoolLimit_NextWriterStartsSmall()
    {
        var (first, next) = WriteThenReopenOnFreshThread(4 * SourceWriter.MaxPooledCapacity);

        Assert.NotSame(first, next);
        Assert.InRange(next.Capacity, 0, SourceWriter.MaxPooledCapacity);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Dispose_BuilderWithinThePoolLimit_NextWriterReusesIt()
    {
        var (first, next) = WriteThenReopenOnFreshThread(SourceWriter.MaxPooledCapacity / 2);

        Assert.Same(first, next);
        Assert.Equal(0, next.Length);
    }

    // The pool is thread-static, and a test thread may hold a pool that earlier tests left at any depth, so both
    // writers are opened on a new thread whose pool starts empty.
    private static (StringBuilder First, StringBuilder Next) WriteThenReopenOnFreshThread(int length)
    {
        StringBuilder? first = null;
        StringBuilder? next = null;
        var thread = new Thread(() =>
        {
            using (var writer = new SourceWriter())
            {
                writer.Append(new string('x', length));
                first = writer.GetBuilder();
            }

            using var reopened = new SourceWriter();
            next = reopened.GetBuilder();
        });
        thread.Start();
        thread.Join();

        Assert.NotNull(first);
        Assert.NotNull(next);
        return (first, next);
    }
}

