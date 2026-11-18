#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Reconverging;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

namespace Kassyi.GSharp.Tests.Cpu.Transpiler.Ir;

/// <summary>Verifies <see cref="IrUniformityClassifier"/>.</summary>
[Category(TestCategory.Compiler)]
public class IrUniformityClassifierTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Classify_ThreadIndexCondition_IsThreadVariant()
    {
        Assert.Equal([IrUniformity.ThreadVariant], UniformitiesOf("ThreadVariantIf"));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Classify_UniformParameterCondition_IsUniform()
    {
        Assert.Equal([IrUniformity.Uniform, IrUniformity.ThreadVariant], UniformitiesOf("UniformIf"));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Classify_SameBodyInADeviceFunction_TreatsParametersAsThreadDependent()
    {
        Assert.Equal([IrUniformity.ThreadVariant, IrUniformity.ThreadVariant],
            UniformitiesOf("UniformIf", isDeviceFunction: true));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Classify_MethodThatDoesNotReachActiveMask_IsNotRequired()
    {
        Assert.Equal([IrUniformity.NotRequired], UniformitiesOf("NotReachingActiveMask"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Classify_AndOperandIf_IsThreadVariantEvenWithAUniformLeftOperand()
    {
        Assert.Equal([IrUniformity.ThreadVariant], UniformitiesOf("AndOperandRegion"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Classify_TernaryIfWithUniformCondition_IsUniform()
    {
        Assert.Equal([IrUniformity.Uniform], UniformitiesOf("TernaryUniform"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Classify_TernaryIfWithThreadVariantCondition_IsThreadVariant()
    {
        Assert.Equal([IrUniformity.ThreadVariant], UniformitiesOf("TernaryVariant"));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Classify_WhileCondition_IsClassified()
    {
        Assert.Equal([IrUniformity.ThreadVariant], UniformitiesOf("ThreadVariantWhile"));
        Assert.Equal([IrUniformity.Uniform], UniformitiesOf("UniformWhile"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Classify_UnmappableCondition_IsThreadVariant()
    {
        var compilation = IrTestCases.Compile(IrLoweringSteps.Cases);
        var method = IrClassifier.Classify(
            IrTestCases.Build(IrLoweringSteps.Cases, IrLoweringSteps.CasesType, "ThreadVariantIf"),
            compilation);
        var boolType = compilation.GetSpecialType(SpecialType.System_Boolean);
        var unmappableSource = new IrSource(method.Source.TreeOrdinal, 0, 1, 0, false);
        var condition = new IrConstant(true, null, boolType, unmappableSource);
        var emptyBlock = new IrBlock(ImmutableArray<IrStatement>.Empty, ImmutableArray<IrVariable>.Empty,
            method.Body.Source.AsSynthesized());
        var handIf = new IrIf(condition, emptyBlock, null, method.Body.Source.AsSynthesized());
        var handBody = new IrBlock([handIf], ImmutableArray<IrVariable>.Empty, method.Body.Source);
        var lowered = new IrSplitLoweredBody(handBody, ImmutableDictionary<IrWhile, IrLoweredCondition>.Empty);

        var classified = IrUniformityClassifier.Classify(method, lowered, compilation,
            isDeviceFunction: false, reachesActiveMask: true);

        Assert.Equal([IrUniformity.ThreadVariant], UniformitiesOf(classified.Body));
    }

    private static IrUniformity[] UniformitiesOf(string method, bool isDeviceFunction = false)
    {
        var steps = IrLoweringSteps.Run(IrLoweringSteps.Cases, IrLoweringSteps.CasesType, method, isDeviceFunction);
        return UniformitiesOf(steps.Reconverged.Body.Body);
    }

    private static IrUniformity[] UniformitiesOf(IrBlock body)
    {
        var result = new List<IrUniformity>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(body))
        {
            switch (node)
            {
                case IrIf @if:
                    result.Add(@if.Uniformity);
                    break;
                case IrWhile @while:
                    result.Add(@while.Uniformity);
                    break;
            }
        }

        return [.. result];
    }
}
