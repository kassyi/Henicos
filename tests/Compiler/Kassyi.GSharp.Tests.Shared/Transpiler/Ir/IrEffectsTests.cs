#nullable enable
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the effects of IR expressions (#1561): which operands interfere, from the classification of the calls,
/// and the cases where the IR follows the meaning that the syntax walk of <see cref="ExpressionEffects"/> does not see.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrEffectsTests
{
    private static Compilation Compilation => IrTestCases.Compile("Ir/IrClassifierCases.cs");

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Of_LocalBeforeACallThatWritesItByRef_Interferes()
    {
        var (left, right) = Operands("LocalPlusSetRef");

        Assert.Equal("{x}", left.Reads.ToString());
        Assert.Equal("{x}", right.Writes.ToString());
        Assert.True(left.InterferesWith(right));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Of_ElementBeforeACallThatWritesMemory_Interferes()
    {
        var (left, right) = Operands("ElementPlusWriteArray");

        Assert.Equal("{memory, data}", left.Reads.ToString());
        Assert.True(right.Writes.Memory);
        Assert.True(left.InterferesWith(right));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Of_ScalarMath_ReadsOnlyItsOperands()
    {
        var effects = IrEffects.Of(IrTestCases.Result(IrClassifierTests.Classify("ScalarMath")), Compilation);

        Assert.Equal("{a, b}", effects.Reads.ToString());
        Assert.True(effects.Writes.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Of_PureDeviceFunction_ReadsMemoryAndWritesNothing()
    {
        var effects = IrEffects.Of(IrTestCases.Result(IrClassifierTests.Classify("CallsPure")), Compilation);

        Assert.Equal("{memory, x}", effects.Reads.ToString());
        Assert.True(effects.Writes.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Of_OutDiscard_WritesNothing()
    {
        // The syntax walk takes the discard as a location in memory
        var effects = IrEffects.Of(IrTestCases.Result(IrClassifierTests.Classify("DiscardOut")), Compilation);

        Assert.True(effects.Writes.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Of_StructMemberThroughImplicitThis_WritesTheReceiver()
    {
        // Count + Next(): Next() changes Count through the implicit this, so the read of Count must come first
        var method = IrTestCases.Build("Ir/IrClassifierCases.cs", "IrClassifierCases.Counter", "ReadThenBump");
        var classified = IrClassifier.Classify(method, Compilation);
        var returned = Assert.IsType<IrReturn>(Assert.Single(classified.Body.Statements));
        var binary = Assert.IsType<IrBinary>(returned.Value);

        var left = IrEffects.Of(binary.Left, Compilation);
        var right = IrEffects.Of(binary.Right, Compilation);

        Assert.Equal(DeviceFunctionEffect.RefParameters, Assert.IsType<IrCall>(binary.Right).Classification.Effect);
        Assert.True(right.Writes.Memory);
        Assert.True(left.InterferesWith(right));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void OfLocation_ArrayElement_ReadsTheArrayAndIndexOnly()
    {
        var binary = (IrBinary)IrTestCases.Result(IrClassifierTests.Classify("ElementPlusWriteArray"));

        var location = IrEffects.OfLocation(binary.Left, Compilation);
        var read = IrEffects.OfLocation(binary.Left, Compilation, readsValue: true);

        Assert.Equal("{data}", location.Reads.ToString());
        Assert.Equal("{data, memory}", Normalize(read.Reads.ToString()));
    }

    // The members of a set in a fixed order
    private static string Normalize(string set) =>
        "{" + string.Join(", ", set.Trim('{', '}').Split(", ").OrderBy(static m => m, StringComparer.Ordinal)) + "}";

    private static (ExpressionEffects Left, ExpressionEffects Right) Operands(string name)
    {
        var binary = (IrBinary)IrTestCases.Result(IrClassifierTests.Classify(name));
        return (IrEffects.Of(binary.Left, Compilation), IrEffects.Of(binary.Right, Compilation));
    }
}
