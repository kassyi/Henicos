#nullable enable
using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Analysis;

/// <summary>
/// Verifies which array reads <see cref="NonNullProof"/> proves non-null (#1409 (Reading an element of a null array in a
/// CUDA kernel fails the launch instead of throwing NullReferenceException)): a non-nullable array that a launch or a
/// call has checked and that nothing in its method can change.
/// </summary>
[Category(TestCategory.Compiler)]
public class NonNullProofTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/NonNullProofCases.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("KernelStructField")]
    [InlineData("DeviceParameter")]
    [InlineData("DeviceStructField")]
    [InlineData("DeviceLocalCopy")]
    public void IsProvenNonNull_CheckedNonNullableArray_IsTrue(string method) =>
        Assert.True(IsProven(method));

    // A nullable or oblivious annotation, a write the method makes, a default value, an element, an extension method
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("KernelNullableField")]
    [InlineData("KernelWrittenStruct")]
    [InlineData("KernelObliviousField")]
    [InlineData("KernelElementField")]
    [InlineData("DeviceNullableParameter")]
    [InlineData("DeviceWrittenParameter")]
    [InlineData("DeviceDefaultParameter")]
    [InlineData("DeviceMutatedStruct")]
    [InlineData("DeviceReassignedLocal")]
    [InlineData("DeviceNewStruct")]
    [InlineData("ExtensionParameter")]
    public void IsProvenNonNull_ArrayThatMayBeNull_IsFalse(string method) =>
        Assert.False(IsProven(method));

    // The launchers check the non-nullable array fields a struct argument reaches through struct fields, nothing else
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Holder", "Inner.Data")]
    [InlineData("Buffers", "Data")]
    [InlineData("Oblivious", "")]
    public void GetNonNullableArrayFields_ListsTheCheckedFields(string type, string fields)
    {
        var symbol = s_compilation.GetTypeByMetadataName($"NonNullProofCases.{type}") ??
                     throw new InvalidOperationException($"Type '{type}' not found.");
        var paths = NonNullProof.GetNonNullableArrayFields(symbol)
            .Select(static path => string.Join(".", path.Select(static f => f.Name)));
        Assert.Equal(fields, string.Join(" ", paths));
    }

    // The host model carries the paths the launchers check, as C# field names
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void KernelParameterShape_CarriesTheCheckedFieldPaths()
    {
        var shape = Kassyi.GSharp.Compiler.Marshaling.KernelParameterShape.Classify(GetMethod("KernelStructField").Parameters[0]);
        Assert.Equal(["Inner.Data"], shape.NonNullableArrayFields.AsImmutableArray().ToArray());
    }

    // The caller checks an argument for a by-value non-nullable array or a struct that reaches one
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("DeviceParameter", true)]
    [InlineData("DeviceNullableParameter", false)]
    [InlineData("DeviceStructField", true)]
    public void NonNullProof_AnnotatedParameterNeedsCheck_FollowsAnnotation(string method, bool expected)
    {
        var parameter = GetMethod(method).Parameters[0];
        Assert.Equal(expected, NonNullProof.NeedsBoundaryCheck(parameter));
    }

    private static bool IsProven(string methodName)
    {
        var method = GetMethod(methodName);
        var declaration = method.DeclaringSyntaxReferences[0].GetSyntax();
        var read = declaration.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single(static d => d.Identifier.ValueText == "read");
        var initializer = read.Initializer?.Value ??
                          throw new InvalidOperationException($"The read of '{methodName}' has no initializer.");
        return NonNullProof.IsProvenNonNull(initializer, s_compilation.GetSemanticModel(declaration.SyntaxTree));
    }

    private static IMethodSymbol GetMethod(string name)
    {
        var type = s_compilation.GetTypeByMetadataName("NonNullProofCases.Reads") ??
                   throw new InvalidOperationException("Type 'NonNullProofCases.Reads' not found.");
        return type.GetMembers(name).OfType<IMethodSymbol>().Single();
    }
}
