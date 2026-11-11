#nullable enable
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the value category the IR builder gives each expression from the way its holder uses it (#1561): ref, out,
/// in and ref readonly arguments, struct receivers, fields of variables and of values, ref returns, ref conditionals,
/// pointers and discards.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrBuilderCategoryTests
{
    // C# classifies these as variables; OperationExtensions.IsVariable does not yet, and adding them changes what the
    // analyses and the CPU lowering pass by reference
    private const string NotAVariableYet =
        "Blocked by #1585 (an inline array element or a ref conditional passed to an in parameter is copied)";

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("InVariable", "Ir.TakeIn(value: in x)")]
    [InlineData("InExplicit", "Ir.TakeIn(value: in x)")]
    [InlineData("InNonVariable", "Ir.TakeIn(value: (x + 1))")]
    [InlineData("RefReadOnlyArgument", "Ir.TakeRefReadOnly(value: in x)")]
    [InlineData("RefArgument", "Ir.TakeRef(value: ref x)")]
    [InlineData("FieldOfVariable", "Ir.TakeIn(value: in (in cell).F)")]
    [InlineData("FieldOfValue", "Ir.TakeIn(value: Ir.MakeCell(j: j).F)")]
    [InlineData("ReferenceField", "Ir.TakeIn(value: in box.Value)")]
    [InlineData("StaticField", "Ir.TakeIn(value: in Ir.s_counter)")]
    [InlineData("ArrayElementIn", "Ir.TakeIn(value: in data[i])")]
    [InlineData("RefReturnProperty", "Ir.TakeRef(value: ref (ref holder).Current)")]
    [InlineData("RefReturnCall", "Ir.TakeRef(value: ref Ir.Pick(data: data))")]
    [InlineData("RefReturnCallRead", "Ir.Pick(data: data)")]
    [InlineData("StructMethodOnVariable", "(ref cell).Peek()")]
    [InlineData("ReadOnlyMethodOnVariable", "(in cell).ReadOnlyPeek()")]
    [InlineData("ReadOnlyStructMethod", "(in frozen).Read()")]
    [InlineData("StructMethodOnValue", "Ir.MakeCell(j: j).Peek()")]
    [InlineData("ComputedRead", "(in cell).Computed")]
    [InlineData("SpanIndexer", "span[index: i]")]
    [InlineData("AddressOf", "Ir.ReadPointer(p: ptr &(ref x))")]
    [InlineData("PointerRead", "Ir.ReadPointer(p: ptr p)")]
    [InlineData("ValueConditional", "(c ? a[0] : b[0])")]
    public void Result_HasTheCategoriesOfItsUses(string method, string expected) =>
        Assert.Equal(expected, IrTestCases.PrintCaseResult(method));

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("RefLocal", 0, "local ref int r = ref data[0]")]
    [InlineData("RefLocal", 1, "(ref r = 5)")]
    [InlineData("RefReadOnlyLocal", 0, "local in int r = in data[0]")]
    [InlineData("RefReassign", 1, "(ref r = ref data[1])")]
    [InlineData("RefConditional", 0, "local ref int r = ref (c ? ref a[0] : ref b[0])")]
    [InlineData("OutVar", 0, "Ir.TakeOut(value: ref var declared)")]
    [InlineData("OutDiscard", 0, "Ir.TakeOut(value: ref _)")]
    [InlineData("NestedFieldWrite", 0, "(ref (ref (ref outer).Inner).F = 1)")]
    [InlineData("PropertyWrite", 0, "(ref (ref cell).Auto = 3)")]
    [InlineData("ThisField", 0, "(ref cell).Bump()")]
    [InlineData("DiscardAssignment", 0, "(ref _ = x)")]
    public void Statement_HasTheCategoriesOfItsUses(string method, int index, string expected) =>
        Assert.Equal(expected, IrPrinter.Print(IrTestCases.Statement(IrTestCases.BuildCase(method), index)));

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("ReturnRef", "return ref data[1]")]
    [InlineData("ReturnRefReadOnly", "return in data[1]")]
    public void ReturnByReference_IsALocation(string method, string expected) =>
        Assert.Equal(expected, IrPrinter.Print(IrTestCases.Statement(IrTestCases.BuildCase(method))));

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ThisOfAStructMember_IsARefParameterOrInWhenReadOnly()
    {
        var compilation = IrTestCases.Compile(IrTestCases.BuilderCases);
        var bump = IrTestCases.Build(IrTestCases.BuilderCases, "IrCases.Cell", "Bump");
        var peek = IrTestCases.Build(IrTestCases.BuilderCases, "IrCases.Cell", "ReadOnlyPeek");
        var read = IrTestCases.Build(IrTestCases.BuilderCases, "IrCases.Frozen", "Read");

        Assert.Equal(RefKind.Ref, bump.This?.RefKind);
        Assert.Equal(RefKind.In, peek.This?.RefKind);
        Assert.Equal(RefKind.In, read.This?.RefKind);
        Assert.Equal("return this.F", IrPrinter.Print(IrTestCases.Statement(peek)));
        Assert.Empty(IrInvariants.Verify(bump, compilation));
    }

    /// <summary>
    /// Every location the builder makes is a variable by <see cref="OperationExtensions.IsVariable"/>, the one rule the
    /// analyses use: for each argument of a by-reference parameter in the cases, the category matches the operation.
    /// </summary>
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("InVariable", true)]
    [InlineData("InNonVariable", false)]
    [InlineData("FieldOfVariable", true)]
    [InlineData("FieldOfValue", false)]
    [InlineData("ReferenceField", true)]
    [InlineData("StaticField", true)]
    [InlineData("ArrayElementIn", true)]
    [InlineData("RefReturnProperty", true)]
    [InlineData("RefReturnCall", true)]
    [InlineData("RefArgument", true)]
    [InlineData("InlineArrayElementIn", true, Skip = NotAVariableYet)]
    [InlineData("RefConditionalIn", true, Skip = NotAVariableYet)]
    [InlineData("PointerIndirectionIn", true, Skip = NotAVariableYet)]
    public void LocationCategory_FollowsIsVariable(string method, bool isVariable)
    {
        var compilation = IrTestCases.Compile(IrTestCases.BuilderCases);
        var symbol = IrTestCases.FindMethod(compilation, "IrCases.Ir", method);
        var syntax = symbol.DeclaringSyntaxReferences[0].GetSyntax();
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var argument = syntax.DescendantNodes()
            .OfType<ArgumentSyntax>()
            .Select(node => model.GetOperation(node))
            .OfType<IArgumentOperation>()
            .First();
        var call = Assert.IsType<IrCall>(IrTestCases.Result(IrTestCases.BuildCase(method)));

        Assert.Equal(isVariable, argument.Value.IsVariable());
        Assert.Equal(isVariable,
            call.Arguments[0].Value.Category is IrCategory.Location or IrCategory.ReadOnlyLocation);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StaticMethod_HasNoThis() => Assert.Null(IrTestCases.BuildCase("Literal").This);
}
