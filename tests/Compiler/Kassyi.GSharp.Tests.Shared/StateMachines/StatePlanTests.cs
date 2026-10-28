using System;
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// The state plan that <c>AstLoweringPipeline</c> gives <see cref="ContextHoister"/> and <see cref="StateMachineLowerer"/>:
/// the lowerer takes the state of each label from the plan and stops when the body it lowers disagrees with it.
/// </summary>
[Category(TestCategory.Compiler)]
public class StatePlanTests
{
    private static readonly DeclarationKey s_again = new(null, "again", 0);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Lower_PlanThatAgreesWithTheBody_LowersTheLabelIntoItsState()
    {
        var plan = new StatePlan([s_again], new Dictionary<DeclarationKey, int> { [s_again] = 2 }, null);

        var lowered = new StateMachineLowerer(false, null, plan).Lower(ReadBody()).NormalizeWhitespace().ToFullString();

        Assert.Contains("case 2:", lowered);
        Assert.Contains("goto case 2;", lowered);
    }

    // A pass between the planner and the lowerer that moved a label or a split point would make a goto case jump into
    // another state than its label's; the lowerer reports it instead
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Lower_PlanThatDisagreesWithTheBody_Throws()
    {
        var plan = new StatePlan([s_again], new Dictionary<DeclarationKey, int> { [s_again] = 1 }, null);

        var ex = Assert.Throws<InvalidOperationException>(() => new StateMachineLowerer(false, null, plan).Lower(ReadBody()));

        Assert.Contains("'again' starts state 2", ex.Message);
    }

    // The kernel body, with its barrier annotated by spelling as a lowering without a semantic model does
    private static BlockSyntax ReadBody()
    {
        var root = CSharpSyntaxTree.ParseText(RoslynTestUtility.ReadTestCase("StateMachines/LabelAfterBarrier.cs")).GetRoot();
        var body = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body
                   ?? throw new InvalidOperationException("The test case kernel has no block body.");
        return TestSyncAnnotator.Annotate(body);
    }
}
