#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The arm that the managed copy of a kernel adds to a switch expression that is not exhaustive
/// (<see cref="ManagedExecutionRewriter.PlanUnmatchedArm"/>): it throws what C# throws when no arm matches.
/// </summary>
[Category(TestCategory.Compiler)]
public class ManagedUnmatchedSwitchArmTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation();

    public static TheoryData<string> BoxableInputs => ["int", "string", "int?"];

    public static TheoryData<string> UnboxableInputs => ["Span<int>", "int*"];

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(BoxableInputs))]
    public void PlanUnmatchedArm_BoxableInput_PassesTheUnmatchedValue(string input)
    {
        var plan = ManagedExecutionRewriter.PlanUnmatchedArm(GetInputType(input), s_compilation);

        Assert.Equal(UnmatchedSwitchArm.SwitchExpressionWithValue, plan);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(UnboxableInputs))]
    public void PlanUnmatchedArm_InputThatCannotBeBoxed_LeavesTheValueOut(string input)
    {
        var plan = ManagedExecutionRewriter.PlanUnmatchedArm(GetInputType(input), s_compilation);

        Assert.Equal(UnmatchedSwitchArm.SwitchExpression, plan);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void PlanUnmatchedArm_LibraryWithoutSwitchExpressionException_ThrowsInvalidOperation()
    {
        var withoutReferences = CSharpCompilation.Create("NoCoreLibrary");
        var input = s_compilation.GetSpecialType(SpecialType.System_Int32);

        var plan = ManagedExecutionRewriter.PlanUnmatchedArm(input, withoutReferences);

        Assert.Equal(UnmatchedSwitchArm.InvalidOperation, plan);
    }

    private static ITypeSymbol GetInputType(string input)
    {
        var int32 = s_compilation.GetSpecialType(SpecialType.System_Int32);
        return input switch
        {
            "int" => int32,
            "string" => s_compilation.GetSpecialType(SpecialType.System_String),
            "int?" => s_compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(int32),
            "Span<int>" => (s_compilation.GetTypeByMetadataName("System.Span`1")
                            ?? throw new InvalidOperationException("System.Span`1 is not referenced")).Construct(int32),
            "int*" => s_compilation.CreatePointerTypeSymbol(int32),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, null)
        };
    }
}
