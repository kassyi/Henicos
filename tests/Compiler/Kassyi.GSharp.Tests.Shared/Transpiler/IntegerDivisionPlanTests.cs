#nullable enable
using Kassyi.GSharp.Compiler.Transpiler.Helpers;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Verifies which integer divisions <see cref="IntegerSemantics"/> writes as a Prelude function with a divisor check
/// (#718 (Array and span bounds, integer division by zero, Nullable.Value, Math.Clamp and Math.Abs(MinValue) are not
/// checked as in C#)) and which keep the C++ operator because the divisor cannot be 0, or -1 for a dividend that may be
/// the minimum of its type.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class IntegerDivisionPlanTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/IntegerDivisionCases.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("VariableDivisor", "__gsharp_div_i32")]
    [InlineData("VariableRemainder", "__gsharp_rem_i32")]
    [InlineData("UnsignedDivisor", "__gsharp_div_u32")]
    [InlineData("LongDivisor", "__gsharp_div_i64")]
    [InlineData("UnsignedLongRemainder", "__gsharp_rem_u64")]
    [InlineData("ByteDivisor", "__gsharp_div_i32")]
    [InlineData("MinusOneDivisor", "__gsharp_div_i32")]
    [InlineData("LongMinusOneRemainder", "__gsharp_rem_i64")]
    [InlineData("CompoundDivision", "__gsharp_div_i32")]
    [InlineData("NarrowCompoundRemainder", "__gsharp_rem_i32")]
    public void Helper_DivisorThatMayBeZeroOrMinusOne_IsTheCheckedPreludeFunction(string name, string expected)
    {
        var helper = GetHelper(name);

        Assert.Equal(expected, helper);
        Assert.True(IntegerSemantics.TakesErrorSite(expected));
    }

    // mutant: NeedsDivisorCheck that always answers true writes every one of these as a Prelude call
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ConstantDivisor")]
    [InlineData("ConstantLongRemainder")]
    [InlineData("UnsignedConstantDivisor")]
    [InlineData("NarrowDividendByMinusOne")]
    [InlineData("UnsignedDividendByMinusOne")]
    [InlineData("CompoundConstantDivision")]
    public void Helper_DivisorProvenNeitherZeroNorMinusOneOfMinimum_KeepsTheOperator(string name) =>
        Assert.Null(GetHelper(name));

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("__gsharp_add_i32")]
    [InlineData("__gsharp_shl_i64")]
    [InlineData("fmodf")]
    public void TakesErrorSite_HelperOtherThanADivision_IsFalse(string helper) =>
        Assert.False(IntegerSemantics.TakesErrorSite(helper));

    private static string? GetHelper(string name)
    {
        var type = s_compilation.GetTypeByMetadataName("IntegerDivisionCases.Divisions") ??
                   throw new InvalidOperationException("Type 'IntegerDivisionCases.Divisions' not found.");
        var method = type.GetMembers(name).OfType<IMethodSymbol>().First();
        var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();
        var body = declaration.ExpressionBody?.Expression ??
                   throw new InvalidOperationException($"Method '{name}' has no expression body.");
        return s_compilation.GetSemanticModel(body.SyntaxTree).GetOperation(body) switch
        {
            IBinaryOperation binary => IntegerSemantics.GetBinaryHelper(binary),
            ICompoundAssignmentOperation compound => IntegerSemantics.GetCompoundHelper(compound),
            var other => throw new InvalidOperationException($"Method '{name}' is a {other?.Kind}.")
        };
    }
}
