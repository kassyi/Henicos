#nullable enable
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// <see cref="SharedMemorySiteKey.Compute"/> keys every member body that creates shared memory apart (#564): the
/// accessors of one property, the expression body of a property and a method each get their own buffer.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class SharedMemorySiteKeyTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Compute_AccessorsAndMethod_GiveEveryOwnerItsOwnKey()
    {
        var keys = ComputeKeysByCount();

        Assert.Equal([1, 2, 3, 4, 5, 6], keys.Keys.Order());
        Assert.Equal(6, keys.Values.Distinct().Count());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Compute_GetterAndSetterOfOneProperty_DifferInKey()
    {
        var keys = ComputeKeysByCount();

        Assert.NotEqual(keys[1], keys[2]);
        Assert.NotEqual(keys[3], keys[4]);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Compute_ExpressionBodiedProperty_DiffersFromTheOtherOwners()
    {
        var keys = ComputeKeysByCount();

        Assert.DoesNotContain(keys[5], keys.Where(static pair => pair.Key != 5).Select(static pair => pair.Value));
    }

    // The creations of the test case in source order, keyed by their count argument
    private static Dictionary<int, ulong> ComputeKeysByCount()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext("Transpiler/SharedMemorySiteKeyOwners.cu.cs");
        var keys = new Dictionary<int, ulong>();

        foreach (var creation in tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var count = creation.ArgumentList?.Arguments[0].Expression as LiteralExpressionSyntax;
            Assert.NotNull(count);
            keys.Add((int)(count.Token.Value ?? 0), SharedMemorySiteKey.Compute(model, creation));
        }

        return keys;
    }
}
