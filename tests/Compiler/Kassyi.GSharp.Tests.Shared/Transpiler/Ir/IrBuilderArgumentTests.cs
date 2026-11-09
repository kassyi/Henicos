#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies how the IR builder lays out arguments (#1561): one per parameter in parameter order, the default values
/// C# supplies, the <see langword="params"/> array, the receiver of an extension method, and the position of each
/// argument in the C# order of evaluation.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrBuilderArgumentTests
{
    private const string NamedType = "Kassyi.GSharp.Tests.Shared.TestCases.DefaultAndNamedArgumentsTestCase";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Arguments_InParameterOrder_WithDefaultsAndEvaluationOrder()
    {
        var kernel = IrTestCases.Build(IrTestCases.NamedArgumentCases, NamedType, "SampleKernel");
        var lines = IrPrinter.Print(kernel).Split('\n').Select(static line => line.TrimEnd('\r')).ToArray();

        Assert.Contains(
            "local int r1 = DefaultAndNamedArgumentsTestCase.ComputeStep(input: data[tid], offset: default 0, " +
            "scale: default 1)", lines);
        Assert.Contains(
            "local int r2 = DefaultAndNamedArgumentsTestCase.ComputeStep(input: data[tid], offset: 5, scale: 2) " +
            "eval(input, scale, offset)", lines);
        Assert.Contains(
            "local int r3 = DefaultAndNamedArgumentsTestCase.ComputeStep(input: data[tid], offset: default 0, " +
            "scale: 4) eval(input, scale, offset)", lines);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_NamedArgumentsOutOfOrder_KeepTheOrderWritten()
    {
        var call = Assert.IsType<IrCall>(IrTestCases.Result(IrTestCases.BuildCase("NamedOutOfOrder")));

        Assert.Equal(["a", "b", "c"], call.Arguments.Select(static argument => argument.Parameter.Name));
        Assert.Equal([1, 2, 0], call.Arguments.Select(static argument => argument.EvaluationIndex));
        Assert.Equal(
            [IrArgumentKind.Explicit, IrArgumentKind.DefaultValue, IrArgumentKind.Explicit],
            call.Arguments.Select(static argument => argument.Kind));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DefaultValues_AreSynthesizedConstantsAtTheCall()
    {
        var call = Assert.IsType<IrCall>(IrTestCases.Result(IrTestCases.BuildCase("Defaults")));

        Assert.Equal("Ir.Offset(a: a, b: default 4, c: default 5)", IrPrinter.Print(call));
        foreach (var argument in call.Arguments.Skip(1))
        {
            var constant = Assert.IsType<IrConstant>(argument.Value);
            Assert.True(constant.Source.Synthesized);
            Assert.Equal(call.Source.Span, constant.Source.Span);
        }
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Params", "Ir.Sum(values: params new int[2] {a, b})")]
    [InlineData("ParamsEmpty", "Ir.Sum(values: params new int[0])")]
    public void Test_ParamsArguments_AreOneArray(string method, string expected)
    {
        var call = Assert.IsType<IrCall>(IrTestCases.Result(IrTestCases.BuildCase(method)));

        Assert.Equal(expected, IrPrinter.Print(call));
        var argument = Assert.Single(call.Arguments);
        Assert.Equal(IrArgumentKind.ParamArray, argument.Kind);
        Assert.IsType<IrArrayCreation>(argument.Value);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ExtensionMethod_IsStaticWithTheReceiverFirst()
    {
        var call = Assert.IsType<IrCall>(IrTestCases.Result(IrTestCases.BuildCase("Extension")));

        Assert.Equal("Extensions.Twice(x: a)", IrPrinter.Print(call));
        Assert.Null(call.Receiver);
        Assert.True(call.Method.IsStatic);
        Assert.Equal(0, call.Arguments[0].EvaluationIndex);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InterferingArguments_KeepTheirOrderAndCategories() =>
        Assert.Equal(
            "Order.Sub(a: Order.Next(counter: ref i), b: Order.Next(counter: ref i))",
            IrTestCases.PrintResult(IrTestCases.OrderCases, "OrderCases.Order", "TwoNextCalls"));
}
