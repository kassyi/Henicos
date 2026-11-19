#nullable enable
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Layout;

/// <summary>
/// <see cref="StructPath"/>: the structs on the current path of one walk, a walk started inside another with its own
/// path, and a construction of a generic struct that counts as on the path.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class StructPathTests
{
    private const string Namespace = "Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs.";

    // A walk that starts (null) while another is open on the same thread, as TransferPlanner.Plan does inside
    // DescribeLayoutDifference, does not see the structs of the outer walk
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Enter_WalkStartedInsideAnOpenWalk_HasAPathOfItsOwn()
    {
        var (compilation, _, _) = RoslynTestUtility.GetTestCaseContext("Layout/CyclicStructs/RepeatedStructFields.cs");
        var pair = GetType(compilation, "Pair");
        using (StructPath.Enter(null, pair))
        {
            // A finished walk leaves its set for the next one on the thread
        }

        using var outer = StructPath.Enter(null, pair);
        using var inner = StructPath.Enter(null, pair);
        using var again = StructPath.Enter(outer.Path, pair);

        Assert.Equal((false, false, true), (outer.IsCycle, inner.IsCycle, again.IsCycle));
    }

    // Leaving a struct takes it off the path: the same struct in a second field is no cycle
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Enter_StructLeftBefore_IsNoCycle()
    {
        var (compilation, _, _) = RoslynTestUtility.GetTestCaseContext("Layout/CyclicStructs/RepeatedStructFields.cs");
        var pair = GetType(compilation, "Pair");
        using var outer = StructPath.Enter(null, GetType(compilation, "Diamond"));
        using (StructPath.Enter(outer.Path, pair))
        {
            // The first field of type Pair
        }

        using var second = StructPath.Enter(outer.Path, pair);

        Assert.False(second.IsCycle);
    }

    // G<G<int>> under G<int> is on the path, since the definition G holds itself by value (CS0523); Twin<Twin<long>>
    // under Twin<long> is not, since Twin holds only its type argument
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Enter_LargerConstructionOfAGenericStruct_IsACycleOnlyWhenTheDefinitionHoldsItself()
    {
        var cyclic = RoslynTestUtility.GetTestCaseContext("Layout/CyclicStructs/GenericCyclicStruct.cu.cs").Compilation;
        var plain = RoslynTestUtility.GetTestCaseContext("Layout/CyclicStructs/RepeatedStructFields.cs").Compilation;
        var g = GetType(cyclic, "GenericCyclicStruct+G`1");
        var twin = GetType(plain, "Twin`1");
        var gInt = g.Construct(cyclic.GetSpecialType(SpecialType.System_Int32));
        var twinLong = twin.Construct(plain.GetSpecialType(SpecialType.System_Int64));

        using var gOuter = StructPath.Enter(null, gInt);
        using var gInner = StructPath.Enter(gOuter.Path, g.Construct(gInt));
        using var twinOuter = StructPath.Enter(null, twinLong);
        using var twinInner = StructPath.Enter(twinOuter.Path, twin.Construct(twinLong));

        Assert.Equal((true, false), (gInner.IsCycle, twinInner.IsCycle));
    }

    private static INamedTypeSymbol GetType(Compilation compilation, string name) =>
        Assert.IsAssignableFrom<INamedTypeSymbol>(compilation.GetTypeByMetadataName(Namespace + name));
}
