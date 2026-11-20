#nullable enable

using Kassyi.GSharp.Compiler.Instantiation;
using Microsoft.CodeAnalysis.Text;
using static Kassyi.GSharp.Tests.Shared.Instantiation.GenericCopyTestSupport;

namespace Kassyi.GSharp.Tests.Shared.Instantiation;

/// <summary>
/// Verifies the closed copies of generic declarations: the planned rewrites keep each call and operator bound to what
/// the declaration binds. What the copies compute is in <see cref="GenericCopyValueTests"/>, where they are in
/// <see cref="GenericCopyLocationTests"/>.
/// </summary>
[Category(TestCategory.Compiler)]
public class GenericCopiesTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_InferredGenericCall_KeepsTheGenericOverload()
    {
        var (copy, set) = CreateCopy("CallsHelper", "System.Int32");

        Assert.True(copy.IsFaithful, Describe(copy));
        var call = Single<InvocationExpressionSyntax>(copy);
        var callee = Assert.IsAssignableFrom<IMethodSymbol>(SymbolOf(set, call));
        Assert.True(callee.IsGenericMethod);
        Assert.Equal(SpecialType.System_Int32, callee.TypeArguments[0].SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_ExtensionCallOnTypeWithSameInstanceMember_KeepsTheExtension()
    {
        var (copy, set) = CreateCopy("CallsExt", "App.Odd");

        Assert.True(copy.IsFaithful, Describe(copy));
        var callee = Assert.IsAssignableFrom<IMethodSymbol>(SymbolOf(set, Single<InvocationExpressionSyntax>(copy)));
        Assert.Equal("Exts", callee.ContainingType.Name);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Sum", "System.Int32")]
    [InlineData("Sum", "App.Vec")]
    [InlineData("Sum", "System.Byte")]
    [InlineData("Sum", "System.Double")]
    [InlineData("CheckedSum", "System.Int32")]
    [InlineData("CheckedSum", "App.Vec")]
    [InlineData("CheckedSum", "System.Byte")]
    [InlineData("Identities", "System.Int32")]
    [InlineData("Identities", "System.Single")]
    [InlineData("Max", "System.Int32")]
    [InlineData("Max", "System.Single")]
    [InlineData("Negate", "System.Int32")]
    [InlineData("Negate", "System.UInt32")]
    [InlineData("Negate", "System.UInt64")]
    [InlineData("Negate", "System.Byte")]
    [InlineData("CheckedNegate", "System.SByte")]
    [InlineData("Shift", "System.Int32")]
    [InlineData("Shift", "System.Byte")]
    [InlineData("ShiftRight", "System.SByte")]
    [InlineData("ShiftRightUnsigned", "System.Int16")]
    [InlineData("ShiftRightUnsigned", "System.Char")]
    [InlineData("CheckedDivide", "System.SByte")]
    [InlineData("CheckedMultiply", "System.UInt16")]
    [InlineData("Complement", "System.Byte")]
    [InlineData("ShiftAssign", "System.Int32")]
    [InlineData("ShiftAssign", "System.Byte")]
    [InlineData("ShiftAssignRightUnsigned", "System.SByte")]
    [InlineData("CompoundDivide", "System.SByte")]
    [InlineData("CheckedCompoundDivide", "System.Int16")]
    [InlineData("CompoundDivideAt", "System.SByte")]
    [InlineData("CheckedCompoundAdd", "System.Byte")]
    [InlineData("CheckedCompoundAdd", "System.Char")]
    [InlineData("CheckedCompoundAdd", "System.Int16")]
    [InlineData("Less", "System.Byte")]
    [InlineData("Less", "System.Char")]
    [InlineData("Increment", "System.Byte")]
    [InlineData("CheckedIncrement", "System.SByte")]
    [InlineData("MinValue", "System.Int32")]
    [InlineData("MaxValue", "System.UInt64")]
    [InlineData("MaxValue", "System.Char")]
    [InlineData("MinValue", "System.Decimal")]
    [InlineData("Pi", "System.Single")]
    [InlineData("Pi", "System.Double")]
    [InlineData("Identities", "System.Decimal")]
    [InlineData("Abs", "System.UInt32")]
    [InlineData("Abs", "System.Int16")]
    [InlineData("AbsPlusB", "System.Byte")]
    [InlineData("AbsPlusB", "System.Char")]
    [InlineData("AbsLessB", "System.Byte")]
    [InlineData("AbsLessB", "System.Char")]
    [InlineData("CompoundAddAbs", "System.Byte")]
    [InlineData("CompoundAddAbs", "System.Char")]
    [InlineData("SignOfAbs", "System.Char")]
    [InlineData("MaxOfAbs", "System.Char")]
    [InlineData("MaxMagnitude", "System.UInt64")]
    [InlineData("MaxMagnitude", "System.Char")]
    [InlineData("MinMagnitude", "System.Char")]
    [InlineData("IsNaN", "System.Int32")]
    [InlineData("IsNaN", "System.Double")]
    [InlineData("IsNaN", "System.Decimal")]
    [InlineData("Sign", "System.Char")]
    [InlineData("Max", "System.Char")]
    [InlineData("Min", "System.Char")]
    [InlineData("OneOverZero", "System.Int32")]
    [InlineData("CheckedMaxValuePlusOne", "System.Byte")]
    [InlineData("CompareTo", "System.Int32")]
    [InlineData("CompareTo", "System.Char")]
    [InlineData("CallFoo", "App.Plain")]
    [InlineData("CallFoo", "App.Both")]
    [InlineData("CallFooOfValue", "App.Both")]
    [InlineData("CallDefault", "App.Defaulted")]
    [InlineData("NextTwice", "App.Counter")]
    [InlineData("AddAndPlus", "App.Counter")]
    [InlineData("NextInArray", "App.Counter")]
    [InlineData("Sum", "System.Int64")]
    [InlineData("Sum", "System.IntPtr")]
    [InlineData("Sum", "System.Decimal")]
    [InlineData("CheckedSum", "System.UIntPtr")]
    [InlineData("Locals", "System.Int32")]
    [InlineData("Recurse", "System.Int32")]
    public void Create_SupportedForm_GivesACopyThatCompiles(string method, string typeArgument)
    {
        var (copy, set) = CreateCopy(method, typeArgument);

        Assert.True(copy.IsFaithful, Describe(copy));
        var errors = ModelOf(set, copy.Declaration!)
            .GetDiagnostics(copy.Declaration!.Span)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id);
        Assert.Empty(errors);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Sum", "App.Odd", "implements", "a + b")]
    [InlineData("CompoundDivideWithEffect", "System.SByte", "has side effects", "data[i++] /= b")]
    public void Create_FormWithoutAClosedSpelling_ReportsItsNode(
        string method,
        string typeArgument,
        string reason,
        string expression)
    {
        var (copy, _) = CreateCopy(method, typeArgument);

        var finding = Assert.Single(copy.Findings);
        Assert.Contains(reason, finding.Reason, StringComparison.Ordinal);
        Assert.Equal(expression, SourceText.From(s_source).ToString(finding.Location.SourceSpan));
    }

    // A sibling member of a generic type is not pinned to the closed type yet: the check stops the copy
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_SiblingOfGenericType_IsRejectedNotEmitted()
    {
        var compilation = CreateCompilation();
        var outer = compilation.GetTypeByMetadataName("App.Outer`1") ??
                    throw new InvalidOperationException("No Outer.");
        var run = outer.Construct(compilation.GetSpecialType(SpecialType.System_Int32)).GetTypeMembers("Kernels")[0]
            .GetMembers("Run").OfType<IMethodSymbol>().Single();

        var copy = GenericCopies.Create(compilation, [GenericInstance.Of(run)]).Copies.Single();

        Assert.False(copy.IsFaithful);
        Assert.Contains(copy.Findings, f => f.Reason.Contains("does not compile", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_NameOfTypeParameter_KeepsTheName()
    {
        var (copy, set) = CreateCopy("Name", "System.Int32");

        Assert.True(copy.IsFaithful, Describe(copy));
        var literal = Single<LiteralExpressionSyntax>(copy);
        Assert.Equal("T", ModelOf(set, literal).GetConstantValue(literal).Value);
    }
}
