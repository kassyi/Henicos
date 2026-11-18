#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Frames;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies <see cref="IrLivenessAnalysis.Analyze"/> and <see cref="IrLivenessInvariants.Verify"/> on each
/// state-machine liveness shape: conditional and loop transfer, compound updates, <see langword="out"/> declarations,
/// stack-array pin exceptions, non-splitting methods, and <see cref="IrResumeCall"/> states.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrLivenessTests
{
    private const string LivenessCases = "Ir/IrLivenessCases.cs";
    private const string LivenessCasesType = "IrLiveness.IrLivenessCases";
    private const string FrameCases = "Transpiler/Frames/StateMachineFrameSource.cs";
    private const string FrameKernelsType = "FrameCases.FrameKernels";
    private const string BarrierKernelsType = "Kassyi.GSharp.Tests.Shared.Syntax.DeviceFunctionBarrierKernels";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Analyze_AssignedInOneArm_KeepsTheVariableLive()
    {
        var liveness = AnalyzeCase("AssignedInOneArm");
        var x = Local(liveness, "x");

        Assert.True(liveness.IsLiveAcrossSourceSplit(x));
        Assert.True(liveness.IsLiveAcrossAnySplit(x));
        Assert.True(liveness.IsTouchedInAnotherState(x));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Analyze_WrittenBeforeRead_IsTouchedButNotLive()
    {
        var liveness = AnalyzeCase("WrittenBeforeRead");
        var x = Local(liveness, "x");

        Assert.True(liveness.IsTouchedInAnotherState(x));
        Assert.False(liveness.IsLiveAcrossSourceSplit(x));
        Assert.False(liveness.IsLiveAcrossAnySplit(x));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Analyze_LoopCarried_KeepsTheAccumulatorAndTheIndexLive()
    {
        var liveness = AnalyzeCase("LoopCarried");
        var s = Local(liveness, "s");
        var i = Local(liveness, "i");

        Assert.True(liveness.IsLiveAcrossSourceSplit(s));
        Assert.True(liveness.IsLiveAcrossSourceSplit(i));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Analyze_ShuffleValue_KeepsTheValueLiveAndNotTheResult()
    {
        var liveness = AnalyzeCase("ShuffleValue");
        var v = Local(liveness, "v");
        var r = Local(liveness, "r");

        Assert.True(liveness.IsLiveAcrossSourceSplit(v));
        Assert.False(liveness.IsLiveAcrossSourceSplit(r));
        Assert.False(liveness.IsLiveAcrossAnySplit(r));
        Assert.False(liveness.IsTouchedInAnotherState(r));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Analyze_CompoundAfterSplit_ReadsTheTarget()
    {
        var liveness = AnalyzeCase("CompoundAfterSplit");
        var x = Local(liveness, "x");

        Assert.True(liveness.IsLiveAcrossSourceSplit(x));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Analyze_WrittenInLoopOnly_DoesNotKillThroughTheLoop()
    {
        var liveness = AnalyzeCase("WrittenInLoopOnly");
        var x = Local(liveness, "x");

        Assert.True(liveness.IsLiveAcrossSourceSplit(x));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Analyze_WrittenInBothArms_KillsThroughTheConditional()
    {
        var liveness = AnalyzeCase("WrittenInBothArms");
        var x = Local(liveness, "x");

        Assert.False(liveness.IsLiveAcrossSourceSplit(x));
        Assert.False(liveness.IsLiveAcrossAnySplit(x));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Analyze_OutVarBeforeSplit_KeepsTheOutVariableLive()
    {
        var liveness = AnalyzeCase("OutVarBeforeSplit");
        var y = Local(liveness, "y");

        Assert.Equal(IrDeclarationForm.Reference, y.Form);
        Assert.True(liveness.IsLiveAcrossSourceSplit(y));
        Assert.True(liveness.IsTouchedInAnotherState(y));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Analyze_UnusedAfterSplit_IsNeitherLiveNorTouched()
    {
        var liveness = AnalyzeCase("UnusedAfterSplit");
        var x = Local(liveness, "x");

        Assert.False(liveness.IsLiveAcrossSourceSplit(x));
        Assert.False(liveness.IsLiveAcrossAnySplit(x));
        Assert.False(liveness.IsTouchedInAnotherState(x));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Analyze_StackArrayPassedInLoop_IsPinned()
    {
        var liveness = AnalyzeCase("StackArrayPinnedInLoop");
        var s = Local(liveness, "s");

        Assert.True(liveness.IsPinned(s));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Analyze_StackArrayElementsOnly_IsNotPinned()
    {
        var liveness = AnalyzeCase("StackArrayElementsOnly");
        var s = Local(liveness, "s");

        Assert.False(liveness.IsPinned(s));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Analyze_ReadBeforeSplitInLoop_KeepsTheValueLiveThroughTheLoop()
    {
        var liveness = AnalyzeCase("ReadBeforeSplitInLoop");
        var k = Local(liveness, "k");

        Assert.True(liveness.IsLiveAcrossSourceSplit(k));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Analyze_NoSplit_ReturnsEmptySets()
    {
        var liveness = AnalyzeCase("NoSplit");
        var x = Local(liveness, "x");

        Assert.False(liveness.HasSplit);
        Assert.All(liveness.Variables, v =>
        {
            Assert.False(liveness.IsLiveAcrossSourceSplit(v));
            Assert.False(liveness.IsLiveAcrossAnySplit(v));
            Assert.False(liveness.IsTouchedInAnotherState(v));
            Assert.False(liveness.IsPinned(v));
        });
        Assert.Equal("x", x.Variable.Name);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Analyze_CallerKernel_KeepsTheArgumentsLive()
    {
        var machine = BuildMachine(IrTestCases.Compile(FrameCases), FrameKernelsType, "CallerKernel");
        var liveness = IrLivenessAnalysis.Analyze(machine);

        var tid = Local(liveness, "tid");
        var x = Local(liveness, "x");
        Assert.True(liveness.IsLiveAcrossSourceSplit(tid));
        Assert.True(liveness.IsLiveAcrossSourceSplit(x));

        var resume = Assert.IsType<IrResumeCall>(machine.States[1].Statements[0]);
        Assert.NotNull(resume.ResultTarget);
        if (resume.ResultTarget.Variable.Kind == IrVariableKind.Temporary)
        {
            var resultVar = liveness[resume.ResultTarget.Variable];
            Assert.False(liveness.IsLiveAcrossSourceSplit(resultVar));
            Assert.False(liveness.IsLiveAcrossAnySplit(resultVar));
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Analyze_OutParameterKernel_DoesNotKeepTheOutDeclarationsLive()
    {
        var machine = BuildMachine(RoslynTestUtility.SharedCompilation, BarrierKernelsType, "OutParameterKernel");
        var liveness = IrLivenessAnalysis.Analyze(machine);

        var tid = Local(liveness, "tid");
        var reversed = Local(liveness, "reversed");
        var original = Local(liveness, "original");

        Assert.True(liveness.IsLiveAcrossSourceSplit(tid));
        Assert.Equal(IrDeclarationForm.Reference, reversed.Form);
        Assert.Equal(IrDeclarationForm.Reference, original.Form);
        Assert.False(liveness.IsLiveAcrossSourceSplit(reversed));
        Assert.False(liveness.IsLiveAcrossAnySplit(reversed));
        Assert.False(liveness.IsLiveAcrossSourceSplit(original));
        Assert.False(liveness.IsLiveAcrossAnySplit(original));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void VerifyLiveness_EveryCase_HasNoViolation()
    {
        var casesCompilation = IrTestCases.Compile(LivenessCases);
        string[] caseNames =
        [
            "AssignedInOneArm", "WrittenBeforeRead", "LoopCarried", "ShuffleValue", "CompoundAfterSplit",
            "WrittenInLoopOnly", "WrittenInBothArms", "OutVarBeforeSplit", "UnusedAfterSplit",
            "StackArrayPinnedInLoop", "StackArrayElementsOnly", "ReadBeforeSplitInLoop", "NoSplit"
        ];
        foreach (var name in caseNames)
        {
            var machine = BuildMachine(casesCompilation, LivenessCasesType, name);
            var liveness = IrLivenessAnalysis.Analyze(machine);
            Assert.Empty(IrLivenessInvariants.Verify(machine, liveness));
        }

        var callerMachine = BuildMachine(IrTestCases.Compile(FrameCases), FrameKernelsType, "CallerKernel");
        Assert.Empty(IrLivenessInvariants.Verify(callerMachine, IrLivenessAnalysis.Analyze(callerMachine)));

        var outMachine = BuildMachine(RoslynTestUtility.SharedCompilation, BarrierKernelsType, "OutParameterKernel");
        Assert.Empty(IrLivenessInvariants.Verify(outMachine, IrLivenessAnalysis.Analyze(outMachine)));
    }

    private static IrLiveness AnalyzeCase(string methodName) =>
        IrLivenessAnalysis.Analyze(BuildMachine(IrTestCases.Compile(LivenessCases), LivenessCasesType, methodName));

    private static IrStateMachine BuildMachine(Compilation compilation, string typeName, string methodName)
    {
        var method = IrTestCases.FindMethod(compilation, typeName, methodName);
        var built = IrBuilder.TryBuild(method, compilation, CancellationToken.None)
            ?? throw new InvalidOperationException($"No IR for {typeName}.{methodName}.");
        var classified = IrClassifier.Classify(built, compilation);
        var lowered = IrSplitLowering.Apply(classified, compilation, out var failure)
            ?? throw new InvalidOperationException(
                $"Split lowering failed for {typeName}.{methodName}: {failure.Kind}");
        var linear = IrLinearizer.Linearize(lowered, compilation);
        var machine = IrStateMachineBuilder.Build(linear, isDeviceFunction: false, built.Body.Source, compilation);
        Assert.Empty(IrStateMachineInvariants.Verify(machine));
        return machine;
    }

    private static IrLivenessVariable Local(IrLiveness liveness, string name) =>
        Assert.Single(liveness.Variables, v => v.Variable.Kind == IrVariableKind.Local && v.Variable.Name == name);
}
