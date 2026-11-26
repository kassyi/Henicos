#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the forms of <see cref="IrPrinter"/> that the builder does not produce yet (#1561): a sequence with its
/// temporaries numbered by name in the order they appear, and the spans of the <see cref="IrPrintOptions.Sources"/>
/// option.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrPrinterTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_NumbersItsTemporariesByName()
    {
        var call = (IrCall)IrTestCases.CaseResult("RefArgument");
        var source = call.Source.AsSynthesized();
        var first = IrVariable.CreateTemporary(call.Type, RefKind.None, "seq", source);
        var second = IrVariable.CreateTemporary(call.Type, RefKind.None, "seq", source);
        var store = new IrExpressionStatement(new IrAssignment(
            new IrVariableReference(first, false, IrCategory.Location, default, source), call, false, call.Type,
            IrCategory.Value, source), source);
        var sum = new IrBinary(new IrOperator<BinaryOperatorKind>(BinaryOperatorKind.Add, null, false),
            new IrVariableReference(first, false, IrCategory.Value, default, source),
            new IrVariableReference(second, false, IrCategory.Value, default, source), call.Type, IrCategory.Value,
            default, source);
        var sequence = new IrSequence([first, second], [store], sum, IrCategory.Value, source);

        Assert.Equal("seq($seq0, $seq1; (ref $seq0 = Ir.TakeRef(value: ref x)); ($seq0 + $seq1))",
            IrPrinter.Print(sequence));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sources_MarkSynthesizedNodes()
    {
        var conversion = IrTestCases.CaseResult("ImplicitNumeric");
        var start = conversion.Source.Start;
        var end = start + conversion.Source.Length;

        Assert.Equal($"implicit<long>(x[{start}..{end}])[~{start}..{end}]",
            IrPrinter.Print(conversion, IrPrintOptions.Sources));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RuntimeCall_RefOutInArguments_PrintsEachModifierOnce()
    {
        var call = (IrCall)IrTestCases.CaseResult("RefArgument");
        var source = call.Source.AsSynthesized();
        var variable = IrVariable.CreateTemporary(call.Type, RefKind.None, "pred", source);
        var location = new IrVariableReference(variable, false, IrCategory.Location, default, source);
        var readOnlyLocation = new IrVariableReference(variable, false, IrCategory.ReadOnlyLocation, default, source);
        var value = new IrVariableReference(variable, false, IrCategory.Value, default, source);
        var runtimeCall = new IrRuntimeCall(
            IrRuntimeFunction.WarpRegionEnter,
            WarpShuffleKind.MatchAll,
            0,
            [
                new IrRuntimeArgument(location, RefKind.Ref),
                new IrRuntimeArgument(location, RefKind.Out),
                new IrRuntimeArgument(readOnlyLocation, RefKind.In),
                new IrRuntimeArgument(value, RefKind.None)
            ],
            "test.cs:1",
            call.Type,
            source);

        Assert.Equal("WarpRegionEnter[MatchAll]#0(ref $pred0, out $pred0, in $pred0, $pred0)",
            IrPrinter.Print(runtimeCall));

        var start = source.Start;
        var end = start + source.Length;
        Assert.Equal(
            $"WarpRegionEnter[MatchAll]#0(ref $pred0[~{start}..{end}], out $pred0[~{start}..{end}], " +
            $"in $pred0[~{start}..{end}], $pred0[~{start}..{end}])[~{start}..{end}]",
            IrPrinter.Print(runtimeCall, IrPrintOptions.Sources));
    }
}
