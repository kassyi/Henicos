#nullable enable
using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Unit tests for <see cref="DiffKernelNames.FromLaunchExpression"/> covering valid member-access parsing,
/// trivia trimming, verbatim suffixes, invalid expression shapes, invalid suffixes, and
/// <c>[CallerArgumentExpression]</c> integration.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Unit)]
public sealed class DiffKernelNamesTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void FromLaunchExpression_ValidMemberAccess_ExtractsClassAndKernel()
    {
        // mutant: DiffKernelNames.cs `methodPart[LaunchPrefix.Length..]` -> `methodPart`
        Assert.Equal(
            "ConditionalAccessDifferentialKernels.ArrayConditionalAccessKernel",
            DiffKernelNames.FromLaunchExpression("ConditionalAccessDifferentialKernels.LaunchArrayConditionalAccessKernel"));
        Assert.Equal(
            "HarnessProbeKernels.UInt32AddMatrixKernel",
            DiffKernelNames.FromLaunchExpression("HarnessProbeKernels.LaunchUInt32AddMatrixKernel"));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("  FooKernels.LaunchBarKernel  ", "FooKernels.BarKernel")]
    [InlineData("\tFooKernels\r\n  .  LaunchBarKernel\n", "FooKernels.BarKernel")]
    public void FromLaunchExpression_WhitespaceTriviaAroundExpression_TrimsAndExtracts(
        string launchExpression,
        string expected)
    {
        Assert.Equal(expected, DiffKernelNames.FromLaunchExpression(launchExpression));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(
        "ConditionalAccessDifferentialKernels.LaunchSuspendingCallInOtherBranchKernel",
        "Access",
        "ConditionalAccessDifferentialKernels.SuspendingCallInOtherBranchKernelAccess")]
    [InlineData(
        "ConditionalAccessDifferentialKernels.LaunchSuspendingCallInOtherBranchKernel",
        "Call",
        "ConditionalAccessDifferentialKernels.SuspendingCallInOtherBranchKernelCall")]
    [InlineData(
        "ConditionalAccessDifferentialKernels.LaunchHolderParameterKernel",
        "WithoutData",
        "ConditionalAccessDifferentialKernels.HolderParameterKernelWithoutData")]
    [InlineData(
        "ConditionalAccessDifferentialKernels.LaunchHolderParameterKernel",
        "WithData",
        "ConditionalAccessDifferentialKernels.HolderParameterKernelWithData")]
    [InlineData(
        "IntegerMinMaxClampDifferentialKernels.LaunchClampSByteKernel",
        ".ReversedBounds",
        "IntegerMinMaxClampDifferentialKernels.ClampSByteKernel.ReversedBounds")]
    [InlineData(
        "NullableDifferentialKernels.LaunchValueAccessKernel",
        "_Null",
        "NullableDifferentialKernels.ValueAccessKernel_Null")]
    [InlineData(
        "NullableDifferentialKernels.LaunchValueAccessKernel",
        "_WithValue",
        "NullableDifferentialKernels.ValueAccessKernel_WithValue")]
    [InlineData(
        "ArrayCallSourceDifferentialKernels.LaunchFieldArrayCallKernel",
        ".Fill.Null",
        "ArrayCallSourceDifferentialKernels.FieldArrayCallKernel.Fill.Null")]
    public void FromLaunchExpression_WithSuffix_AppendsSuffixVerbatim(
        string launchExpression,
        string suffix,
        string expected)
    {
        Assert.Equal(expected, DiffKernelNames.FromLaunchExpression(launchExpression, suffix));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("LaunchSegmentSliceKernel")]
    [InlineData("FooKernels.ArrayConditionalAccessKernel")]
    [InlineData("FooKernels.Launch")]
    [InlineData(".LaunchBarKernel")]
    [InlineData("FooKernels.")]
    [InlineData("FooKernels.LaunchBarKernelPtr")]
    [InlineData("Namespace.FooKernels.LaunchBarKernel")]
    [InlineData("global::FooKernels.LaunchBarKernel")]
    [InlineData("(b, s) => FooKernels.LaunchBarKernel(b, s)")]
    [InlineData("FooKernels.LaunchBarKernel<int>")]
    public void FromLaunchExpression_InvalidExpressionShape_ThrowsArgumentException(string launchExpression)
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => DiffKernelNames.FromLaunchExpression(launchExpression, paramName: "launch"));
        Assert.Equal("launch", ex.ParamName);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FromLaunchExpression_NullExpression_ThrowsArgumentNullException()
    {
        string? nullExpression = null;

        // Intentionally passing null! to assert the ArgumentException.ThrowIfNullOrWhiteSpace guard.
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
            () => DiffKernelNames.FromLaunchExpression(nullExpression!, paramName: "launch"));
        Assert.Equal("launch", ex.ParamName);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData(" Access")]
    [InlineData("Access ")]
    public void FromLaunchExpression_InvalidSuffix_ThrowsArgumentException(string suffix)
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => DiffKernelNames.FromLaunchExpression("FooKernels.LaunchBarKernel", suffix));
        Assert.Equal("suffix", ex.ParamName);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void FromLaunchExpression_CallerArgumentExpressionIntegration_DerivesExpectedName()
    {
        Assert.Equal(
            "ConditionalAccessDifferentialKernels.ArrayConditionalAccessKernel",
            Derive(ConditionalAccessDifferentialKernels.LaunchArrayConditionalAccessKernel));
        Assert.Equal(
            "ConditionalAccessDifferentialKernels.ArrayConditionalAccessKernelAccess",
            Derive(ConditionalAccessDifferentialKernels.LaunchArrayConditionalAccessKernel, "Access"));
    }

    private static string Derive(
        Int32RowsLaunch launch,
        string? suffix = null,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "")
    {
        _ = launch;
        return DiffKernelNames.FromLaunchExpression(launchExpression, suffix, nameof(launch));
    }
}
