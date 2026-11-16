using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A user-defined operator in GPU code that no dialect defines (GSH0060), whatever the namespace of its type: the
/// binary, compound assignment and unary operators of <c>System.Numerics</c> types and the increment and decrement of a
/// struct of the project. Built-in operators and an operator in the arguments of a call that C# removes are not
/// reported; <c>[GpuOperatorMacro]</c> operators are covered by
/// <see cref="Analyzers.UserDefinedOperatorAnalyzerTests"/>.
/// </summary>
[Category(TestCategory.Compiler)]
public class UnmappedOperatorDiagnosticsTests
{
    private const string TestCase = "Diagnostics/UnmappedOperator.cs";

    // Quaternion, Matrix4x4 and Complex *, Vector3 +, += and unary - (lines 31 to 37), Counter ++ and -- (lines 47 and
    // 48): the operator token, once, from the analyzer
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnmappedOperator_OperatorWithoutDeviceDefinition_ReportsGsh0060AtOperator(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 31, 25, 31, 26),
                (Analyzer, 32, 25, 32, 26),
                (Analyzer, 33, 25, 33, 26),
                (Analyzer, 34, 25, 34, 26),
                (Analyzer, 36, 15, 36, 17),
                (Analyzer, 37, 20, 37, 21),
                (Analyzer, 47, 14, 47, 16),
                (Analyzer, 48, 13, 48, 15)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UserDefinedOperatorNotSupported.Id));
    }

    // The operator method and the type that declares it
    [Theory]
    [InlineData("Cpu", "'op_Multiply' of 'Quaternion'")]
    [InlineData("Cpu", "'op_Multiply' of 'Matrix4x4'")]
    [InlineData("Cpu", "'op_Multiply' of 'Complex'")]
    [InlineData("Cpu", "'op_UnaryNegation' of 'Vector3'")]
    [InlineData("Cuda", "'op_Increment' of 'Counter'")]
    [InlineData("Cuda", "'op_Decrement' of 'Counter'")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnmappedOperator_DiagnosticMessage_NamesTheOperatorAndItsType(string dialect, string named)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        var message = Assert.Single(
            diagnostics,
            d => d.Id == GSharpDiagnostics.UserDefinedOperatorNotSupported.Id && d.Message.Contains(named)).Message;
        Assert.Equal(
            $"The user-defined operator {named} is not supported in GPU code. " +
            "Call a [GpuDeviceFunction] method instead.",
            message);
    }

    // Vector3 + and * inside a [Conditional] call that C# removes, the built-in operators of float and int, and the
    // user-defined == of Span<T> with null, which is written as a null test
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task BuiltInOrRemovedOperator_ValidOperators_IsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.DoesNotContain(
            diagnostics,
            static d => d.Id == GSharpDiagnostics.UserDefinedOperatorNotSupported.Id && d.StartLine >= 52);
    }

    // Without the analyzers, as in the projects under tests/ (#463), the generator states each operator it cannot write
    // in the native source with its place in the user's file, in every dialect, instead of the C++ operator
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnmappedOperator_GeneratedSource_StatesGsh0060WithLocation(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.Equal(
            [
                ("(31,25): GSH0060", true),
                ("(32,25): GSH0060", true),
                ("(33,25): GSH0060", true),
                ("(34,25): GSH0060", true),
                ("(36,15): GSH0060", true),
                ("(37,20): GSH0060", true),
                ("(47,14): GSH0060", true),
                ("(48,13): GSH0060", true)
            ],
            KernelDiagnostics.FindStatedFailures(run)
                .Where(static f => f.Failure.EndsWith(GSharpDiagnostics.UserDefinedOperatorNotSupported.Id))
                .Order());
    }
}
