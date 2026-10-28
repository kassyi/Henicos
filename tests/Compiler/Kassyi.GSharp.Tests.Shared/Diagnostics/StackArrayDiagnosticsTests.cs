using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// G# keeps a local array, the stack array of a span and the storage of a <c>stackalloc</c> in a C++ array of the
/// innermost scope around it. A use that would outlive that scope is GSH0091; an array that cannot be a stack array is
/// GSH0003 (multidimensional, zero length), and an array created outside a declaration is GSH0001 (#623, Z4, Z5, Z8).
/// </summary>
[Category(TestCategory.Compiler)]
public class StackArrayDiagnosticsTests
{
    private const string Escapes = "Analyzers/StackArrays/StackArrayEscapes.cu.cs";
    private const string Shapes = "Analyzers/StackArrays/StackArrayShapes.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StackArrayThatOutlivesItsScope_ReportsGsh0091AtTheUse(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(Escapes, dialect);

        Assert.Equal(
            [
                (Analyzer, 21, 16, 21, 17),
                (Analyzer, 32, 9, 32, 18),
                (Analyzer, 33, 14, 33, 19),
                (Analyzer, 42, 9, 42, 29),
                (Analyzer, 43, 9, 43, 22),
                (Analyzer, 54, 13, 54, 23),
                (Analyzer, 65, 20, 65, 41),
                (Analyzer, 78, 17, 78, 26),
                (Analyzer, 94, 26, 94, 35),
                (Analyzer, 111, 36, 111, 45),
                (Analyzer, 122, 16, 122, 24),
                (Analyzer, 133, 13, 133, 32),
                (Analyzer, 134, 13, 134, 50),
                (Analyzer, 147, 9, 147, 18),
                (Analyzer, 148, 9, 148, 22),
                (Analyzer, 159, 13, 159, 22),
                (Analyzer, 181, 13, 181, 26),
                (Analyzer, 184, 17, 184, 26),
                (Analyzer, 253, 9, 253, 23),
                (Analyzer, 254, 15, 254, 20),
                (Analyzer, 266, 13, 266, 22),
                (Analyzer, 284, 42, 284, 51),
                (Analyzer, 296, 13, 296, 34),
                (Analyzer, 313, 13, 313, 46),
                (Analyzer, 315, 13, 315, 53),
                (Analyzer, 341, 13, 341, 38),
                (Analyzer, 342, 13, 342, 39)
            ],
            diagnostics.Ranges(GSharpDiagnostics.StackArrayEscapes.Id));
    }

    // The message names the storage and the use, so that the user sees which rule applies
    [Theory]
    [InlineData("'a' cannot be returned", 21)]
    [InlineData("'a' cannot be reassigned", 32)]
    [InlineData("'a' cannot be passed by ref or out", 33)]
    [InlineData("'a' cannot be stored in a field, an element, a parameter or a ref local", 42)]
    [InlineData("'cur' cannot be assigned to a variable declared outside its scope", 54)]
    [InlineData("'stackalloc' cannot be assigned to a variable declared outside its scope", 65)]
    [InlineData("'a' cannot be assigned to a variable declared outside its scope", 78)]
    [InlineData("'a' cannot be assigned to a variable declared outside its scope", 94)]
    [InlineData("'a' cannot be passed to a call with a ref or out argument that outlives it", 111)]
    [InlineData("'a' cannot be returned", 122)]
    [InlineData("'a' cannot be assigned to a variable declared outside its scope", 133)]
    [InlineData("'a' cannot be assigned to a variable declared outside its scope", 134)]
    [InlineData("'a' cannot be stored in a field, an element, a parameter or a ref local", 147)]
    [InlineData("'a' cannot be stored in a field, an element, a parameter or a ref local", 148)]
    [InlineData("'a' cannot be assigned to a variable declared outside its scope", 159)]
    [InlineData("'a' cannot be assigned to a variable declared outside its scope", 181)]
    [InlineData("'a' cannot be assigned to a variable declared outside its scope", 184)]
    [InlineData("'s' cannot be reassigned", 253)]
    [InlineData("'s' cannot be passed by ref or out", 254)]
    [InlineData("'collection expression' cannot be assigned to a variable declared outside its scope", 266)]
    [InlineData("'collection expression' cannot be passed to a call with a ref or out argument that outlives it", 284)]
    [InlineData("'stackalloc' cannot be assigned to a variable declared outside its scope", 296)]
    [InlineData("'a' cannot be assigned to a variable declared outside its scope", 313)]
    [InlineData("'s' cannot be assigned to a variable declared outside its scope", 315)]
    [InlineData("'a' cannot be assigned to a variable declared outside its scope", 341)]
    [InlineData("'a' cannot be assigned to a variable declared outside its scope", 342)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StackArrayEscape_MessageNamesTheStorageAndTheUse(string messagePart, int line)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(Escapes, "Cuda");

        Assert.Contains(
            diagnostics.Ranges(GSharpDiagnostics.StackArrayEscapes.Id, messagePart),
            range => range.StartLine == line);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_MultidimensionalArray_ReportsGsh0003AtTheCreation(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(Shapes, dialect);

        Assert.Equal(
            [(Analyzer, 17, 20, 17, 33)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'multidimensional array'"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DynamicStackAllocInAnArgument_ReportsGsh0003AtTheSize(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(Shapes, dialect);

        Assert.Equal(
            [(Analyzer, 45, 43, 45, 44)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'dynamic array size'"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_ZeroLengthArray_ReportsGsh0003AtTheCreation(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(Shapes, dialect);

        Assert.Equal(
            [
                (Analyzer, 25, 19, 25, 29),
                (Analyzer, 26, 19, 26, 22),
                (Analyzer, 27, 19, 27, 21)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'zero-length array'"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ArrayCreatedOutsideADeclaration_ReportsGsh0001Once(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(Shapes, dialect);

        Assert.Equal(
            [(Analyzer, 35, 25, 35, 35)],
            diagnostics.Ranges(GSharpDiagnostics.ObjectCreationNotSupported.Id));
        // The transpiler used to report GSH0003 'ArrayCreationExpression' here
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "ArrayCreationExpression"));
    }
}
