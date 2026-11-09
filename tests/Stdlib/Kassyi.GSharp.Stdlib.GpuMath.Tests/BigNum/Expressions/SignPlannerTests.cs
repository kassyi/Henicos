#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of the sign plan of the signed nodes, without a GPU:
// - a sign the host can derive from host signs alone is planned on the host, with the C# / BigInteger rule
//   (same-sign sums keep the sign, products and quotients XOR, a non-negative dividend gives a + remainder,
//   ~ of a non-negative value is negative, -x flips, Fit keeps);
// - a sign that depends on the values (a borrow, the operand Max / Min choose, a zero remainder, ~ of a negative
//   value) or on an operand's device sign is planned as the kernel that writes it on the device.

/// <summary><see cref="SignPlanner"/> over every operation and kind of operand sign.</summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class SignPlannerTests
{
    // Operand sign kinds: "+" and "-" on the host, "d" on the device
    public static TheoryData<string, string, string, string> Plans() => new()
    {
        // mutant: SignPlan.cs Add/Subtract `first.HostNegative == effectiveSecond` -> `!=`
        { nameof(SignedOperation.Add), "+", "+", "+" },
        { nameof(SignedOperation.Add), "-", "-", "-" },
        { nameof(SignedOperation.Add), "+", "-", "AddSubtract" },
        { nameof(SignedOperation.Add), "-", "+", "AddSubtract" },
        { nameof(SignedOperation.Add), "d", "+", "AddSubtract" },
        { nameof(SignedOperation.Add), "+", "d", "AddSubtract" },
        // mutant: the effective sign of the subtrahend without `^ (operation == SignedOperation.Subtract)`
        { nameof(SignedOperation.Subtract), "+", "-", "+" },
        { nameof(SignedOperation.Subtract), "-", "+", "-" },
        { nameof(SignedOperation.Subtract), "+", "+", "AddSubtract" },
        { nameof(SignedOperation.Subtract), "-", "-", "AddSubtract" },
        { nameof(SignedOperation.Subtract), "d", "d", "AddSubtract" },
        // mutant: Multiply/Divide `^` -> `|`
        { nameof(SignedOperation.Multiply), "+", "+", "+" },
        { nameof(SignedOperation.Multiply), "-", "-", "+" },
        { nameof(SignedOperation.Multiply), "-", "+", "-" },
        { nameof(SignedOperation.Multiply), "+", "d", "Xor" },
        { nameof(SignedOperation.Divide), "+", "-", "-" },
        { nameof(SignedOperation.Divide), "-", "-", "+" },
        { nameof(SignedOperation.Divide), "d", "+", "Xor" },
        // mutant: Remainder planned on the host for a negative dividend
        { nameof(SignedOperation.Remainder), "+", "-", "+" },
        { nameof(SignedOperation.Remainder), "+", "d", "+" },
        { nameof(SignedOperation.Remainder), "-", "+", "RemainderSign" },
        { nameof(SignedOperation.Remainder), "d", "+", "RemainderSign" },
        { nameof(SignedOperation.Negate), "+", "+", "-" },
        { nameof(SignedOperation.Negate), "-", "+", "+" },
        { nameof(SignedOperation.Negate), "d", "+", "Xor" },
        // mutant: Not of a negative value planned on the host (it misses -0)
        { nameof(SignedOperation.Not), "+", "+", "-" },
        { nameof(SignedOperation.Not), "-", "+", "NotOfNegative" },
        { nameof(SignedOperation.Not), "d", "+", "NotOfNegative" },
        // mutant: Max/Min planned on the host for equal host signs
        { nameof(SignedOperation.Max), "+", "+", "Select" },
        { nameof(SignedOperation.Max), "-", "d", "Select" },
        { nameof(SignedOperation.Min), "-", "-", "Select" },
        { nameof(SignedOperation.Fit), "-", "+", "-" },
        { nameof(SignedOperation.Fit), "d", "+", "Forward" }
    };

    [Theory]
    [MemberData(nameof(Plans))]
    [Scenario(TestScenario.Boundary)]
    public void Plan_FollowsTheSignRules(string operationName, string first, string second, string expected)
    {
        var operation = Enum.Parse<SignedOperation>(operationName);
        using var a = Source(first);
        using var b = Source(second);

        var plan = SignPlanner.For(operation, a, b);

        // A host plan carries the sign; a device plan names the kernel that writes it
        if (expected is "+" or "-")
        {
            Assert.True(plan.IsHost, $"{operation} {first} {second}: {plan}");
            Assert.Equal(expected == "-", plan.HostNegative);
        }
        else
        {
            Assert.False(plan.IsHost, $"{operation} {first} {second}: {plan}");
            Assert.Equal(Enum.Parse<SignKernel>(expected), plan.Kernel);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void SignedValue_WithADeviceSign_IsNotReadAsAHostSign()
    {
        // The root reads a device sign before handing the value out; ToHost refuses one that was not read
        var value = new SignedValue(default, SignSource.Device(default));
        Assert.Throws<InvalidOperationException>(() => value.ToHost());
        Assert.True(new SignedValue(default, SignSource.Host(true)).ToHost().IsNegative);
    }

    // A device source owns a word; the planner reads only the kind, so a default word (no buffer) stands for it
    private static SignSource Source(string kind) => kind switch
    {
        "+" => SignSource.Host(false),
        "-" => SignSource.Host(true),
        _ => SignSource.Device(default)
    };
}
