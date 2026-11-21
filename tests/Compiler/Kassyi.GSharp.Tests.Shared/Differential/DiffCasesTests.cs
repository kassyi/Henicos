#nullable enable
using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Unit tests for <see cref="DiffLaunches"/> and <see cref="DiffCases"/> covering uncast method-group binding
/// without <c>CS0121</c> ambiguity, generic explicit type-argument usage, <c>shape</c>/<c>shapes</c> validation,
/// <c>oracleIsLaunch</c> defaults, and unforwarded helper <c>[CallerArgumentExpression]</c> rejection.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Unit)]
public sealed class DiffCasesTests
{
    private static DiffBuffers CreateDummyBuffers(DiffRandom rng, LaunchShape shape)
    {
        _ = rng;
        _ = shape;
        return new DiffBuffers();
    }

    private static DifferentialCase CreateWithoutForwardedExpression(Int32PairLaunch launch) =>
        DiffCases.Create(launch, inputs: CreateDummyBuffers, shape: LaunchShapes.SingleThread());

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BindAndCreate_Int32PairMethodGroup_CompilesWithoutAmbiguityAndDerivesName()
    {
        Action<DiffBuffers, LaunchShape> bound =
            DiffLaunches.Bind(ConditionalAccessDifferentialKernels.LaunchSuspendingReceiverKernel);
        Assert.NotNull(bound);

        DifferentialCase diffCase = DiffCases.Create(
            ConditionalAccessDifferentialKernels.LaunchSuspendingReceiverKernel,
            inputs: CreateDummyBuffers,
            shape: LaunchShapes.SingleThread());

        Assert.Equal("ConditionalAccessDifferentialKernels.SuspendingReceiverKernel", diffCase.Name);
        Assert.True(diffCase.OracleIsLaunch);
        Assert.Equal(LaunchShapes.SingleThread(), diffCase.Shape);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BindNullablePairAndCreateNullablePair_Int32NullablePairMethodGroup_CompilesWithoutAmbiguityAndDerivesName()
    {
        Action<DiffBuffers, LaunchShape> bound =
            DiffLaunches.BindNullablePair(ThrowOrderDifferentialKernels.LaunchCallThenLengthKernel);
        Assert.NotNull(bound);

        DifferentialCase diffCase = DiffCases.CreateNullablePair(
            ThrowOrderDifferentialKernels.LaunchCallThenLengthKernel,
            inputs: CreateDummyBuffers,
            shape: LaunchShapes.SingleThread(),
            oracleExceptions: OracleExceptionMode.MatchOracle);

        Assert.Equal("ThrowOrderDifferentialKernels.CallThenLengthKernel", diffCase.Name);
        Assert.True(diffCase.OracleIsLaunch);
        Assert.Equal(OracleExceptionMode.MatchOracle, diffCase.OracleExceptions);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_BothShapeAndShapesProvided_ThrowsArgumentException()
    {
        LaunchShape singleShape = LaunchShapes.SingleThread();

        ArgumentException ex = Assert.Throws<ArgumentException>(() => DiffCases.Create(
            ConditionalAccessDifferentialKernels.LaunchSuspendingReceiverKernel,
            inputs: CreateDummyBuffers,
            shape: singleShape,
            shapes: [singleShape]));

        Assert.Equal("shapes", ex.ParamName);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_BothShapeAndEmptyShapesProvided_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => DiffCases.Create(
            ConditionalAccessDifferentialKernels.LaunchSuspendingReceiverKernel,
            inputs: CreateDummyBuffers,
            shape: LaunchShapes.SingleThread(),
            shapes: []));

        Assert.Equal("shapes", ex.ParamName);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_MissingShapeAndShapes_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => DiffCases.Create(
            ConditionalAccessDifferentialKernels.LaunchSuspendingReceiverKernel,
            inputs: CreateDummyBuffers));

        Assert.Equal("shape", ex.ParamName);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_UnforwardedHelperPassingLaunchParameter_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CreateWithoutForwardedExpression(ConditionalAccessDifferentialKernels.LaunchSuspendingReceiverKernel));

        Assert.Equal("launch", ex.ParamName);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_GenericOverloadsWithExplicitTypeArguments_CompileAndDeriveNames()
    {
        Action<DiffBuffers, LaunchShape> boundPair =
            DiffLaunches.Bind<float, double>(MathDifferentialKernels.LaunchMathOfSingleMatrixKernel);
        Action<DiffBuffers, LaunchShape> boundTripletRows = DiffLaunches.BindRows<int, long, int>(
            BitOperationsPowerOfTwoDifferentialKernels.LaunchTrailingZeroCountSignedKernel,
            firstInputName: "iVals",
            secondInputName: "lVals");

        Assert.NotNull(boundPair);
        Assert.NotNull(boundTripletRows);

        DifferentialCase singleToDouble = DiffCases.Create<float, double>(
            MathDifferentialKernels.LaunchMathOfSingleMatrixKernel,
            inputs: CreateDummyBuffers,
            shape: LaunchShapes.SingleThread());
        DifferentialCase trailingZeroCount = DiffCases.CreateRows<int, long, int>(
            BitOperationsPowerOfTwoDifferentialKernels.LaunchTrailingZeroCountSignedKernel,
            inputs: CreateDummyBuffers,
            shape: LaunchShapes.SingleThread(),
            firstInputName: "iVals",
            secondInputName: "lVals");

        Assert.Equal("MathDifferentialKernels.MathOfSingleMatrixKernel", singleToDouble.Name);
        Assert.Equal("BitOperationsPowerOfTwoDifferentialKernels.TrailingZeroCountSignedKernel", trailingZeroCount.Name);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_OracleIsLaunchDefaultAndExplicitFalse_SetsExpectedFlag()
    {
        DifferentialCase verifyOnlyCase = DiffCases.Create(
            ConditionalAccessDifferentialKernels.LaunchSuspendingReceiverKernel,
            inputs: CreateDummyBuffers,
            shape: LaunchShapes.SingleThread(),
            oracleIsLaunch: false,
            verify: static (_, _) => PropertyResult.Pass());
        DifferentialCase referenceCase = DiffCases.Create(
            ConditionalAccessDifferentialKernels.LaunchSuspendingReceiverKernel,
            inputs: CreateDummyBuffers,
            shape: LaunchShapes.SingleThread(),
            reference: static (_, _) => { });

        Assert.False(verifyOnlyCase.OracleIsLaunch);
        Assert.False(referenceCase.OracleIsLaunch);
    }
}
