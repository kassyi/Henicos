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
        var call = (IrCall)IrTestCases.Result(IrTestCases.BuildCase("RefArgument"));
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
        var conversion = IrTestCases.Result(IrTestCases.BuildCase("ImplicitNumeric"));
        var start = conversion.Source.Start;
        var end = start + conversion.Source.Length;

        Assert.Equal($"implicit<long>(x[{start}..{end}])[~{start}..{end}]",
            IrPrinter.Print(conversion, IrPrintOptions.Sources));
    }
}
