#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of the sign plan of the signed nodes, without a GPU:
// - a sign the host can derive from host signs alone is planned on the host, with the C# / BigInteger rule
//   (same-sign sums keep the sign, products and quotients XOR, a non-negative dividend gives a + remainder,
//   ~ of a non-negative value is negative, -x flips, Fit keeps), and Max / Min of different host signs choose the
//   non-negative / the negative operand without a comparison;
// - a sign that depends on the values (a borrow, the operand Max / Min choose for the same host signs, a zero
//   remainder, ~ of a negative value) or on an operand's device sign is planned as the kernel that writes it on the
//   device.

/// <summary><see cref="SignPlanner"/> over every operation and kind of operand sign.</summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class SignPlannerTests
{
    // Every operation over every kind of operand sign: "+" and "-" on the host, "d" on the device (a unary operation
    // takes "+" as its unused second operand). Expected: a host sign, a kernel, the operand Max / Min choose with its
    // sign ("first +"), or a kernel that computes the value with the host sign it keeps ("Select +"). 7 binary
    // operations x 9 + 3 unary ones x 3 = 72 rows.
    public static TheoryData<string, string, string, string> Plans() => new()
    {
        // mutant: PlanAddSubtract `first.HostNegative == effectiveSecond` -> `!=`
        { nameof(SignedOperation.Add), "+", "+", "+" },
        { nameof(SignedOperation.Add), "+", "-", "AddSubtract" },
        { nameof(SignedOperation.Add), "+", "d", "AddSubtract" },
        { nameof(SignedOperation.Add), "-", "+", "AddSubtract" },
        { nameof(SignedOperation.Add), "-", "-", "-" },
        { nameof(SignedOperation.Add), "-", "d", "AddSubtract" },
        { nameof(SignedOperation.Add), "d", "+", "AddSubtract" },
        { nameof(SignedOperation.Add), "d", "-", "AddSubtract" },
        { nameof(SignedOperation.Add), "d", "d", "AddSubtract" },
        // mutant: the effective sign of the subtrahend without `^ subtract`
        { nameof(SignedOperation.Subtract), "+", "+", "AddSubtract" },
        { nameof(SignedOperation.Subtract), "+", "-", "+" },
        { nameof(SignedOperation.Subtract), "+", "d", "AddSubtract" },
        { nameof(SignedOperation.Subtract), "-", "+", "-" },
        { nameof(SignedOperation.Subtract), "-", "-", "AddSubtract" },
        { nameof(SignedOperation.Subtract), "-", "d", "AddSubtract" },
        { nameof(SignedOperation.Subtract), "d", "+", "AddSubtract" },
        { nameof(SignedOperation.Subtract), "d", "-", "AddSubtract" },
        { nameof(SignedOperation.Subtract), "d", "d", "AddSubtract" },
        // mutant: PlanProductSign `^` -> `|`
        { nameof(SignedOperation.Multiply), "+", "+", "+" },
        { nameof(SignedOperation.Multiply), "+", "-", "-" },
        { nameof(SignedOperation.Multiply), "+", "d", "Xor" },
        { nameof(SignedOperation.Multiply), "-", "+", "-" },
        { nameof(SignedOperation.Multiply), "-", "-", "+" },
        { nameof(SignedOperation.Multiply), "-", "d", "Xor" },
        { nameof(SignedOperation.Multiply), "d", "+", "Xor" },
        { nameof(SignedOperation.Multiply), "d", "-", "Xor" },
        { nameof(SignedOperation.Multiply), "d", "d", "Xor" },
        // The Average node (a division by a + host count) plans as the rows with a + divisor
        { nameof(SignedOperation.Divide), "+", "+", "+" },
        { nameof(SignedOperation.Divide), "+", "-", "-" },
        { nameof(SignedOperation.Divide), "+", "d", "Xor" },
        { nameof(SignedOperation.Divide), "-", "+", "-" },
        { nameof(SignedOperation.Divide), "-", "-", "+" },
        { nameof(SignedOperation.Divide), "-", "d", "Xor" },
        { nameof(SignedOperation.Divide), "d", "+", "Xor" },
        { nameof(SignedOperation.Divide), "d", "-", "Xor" },
        { nameof(SignedOperation.Divide), "d", "d", "Xor" },
        // mutant: PlanRemainder planned on the host for a negative dividend
        { nameof(SignedOperation.Remainder), "+", "+", "+" },
        { nameof(SignedOperation.Remainder), "+", "-", "+" },
        { nameof(SignedOperation.Remainder), "+", "d", "+" },
        { nameof(SignedOperation.Remainder), "-", "+", "RemainderSign" },
        { nameof(SignedOperation.Remainder), "-", "-", "RemainderSign" },
        { nameof(SignedOperation.Remainder), "-", "d", "RemainderSign" },
        { nameof(SignedOperation.Remainder), "d", "+", "RemainderSign" },
        { nameof(SignedOperation.Remainder), "d", "-", "RemainderSign" },
        { nameof(SignedOperation.Remainder), "d", "d", "RemainderSign" },
        // mutant: PlanSelect without the different-host-signs case (a comparison for every value), or Max and Min
        // choosing the same operand
        { nameof(SignedOperation.Max), "+", "+", "Select +" },
        { nameof(SignedOperation.Max), "+", "-", "first +" },
        { nameof(SignedOperation.Max), "+", "d", "Select" },
        { nameof(SignedOperation.Max), "-", "+", "second +" },
        { nameof(SignedOperation.Max), "-", "-", "Select -" },
        { nameof(SignedOperation.Max), "-", "d", "Select" },
        { nameof(SignedOperation.Max), "d", "+", "Select" },
        { nameof(SignedOperation.Max), "d", "-", "Select" },
        { nameof(SignedOperation.Max), "d", "d", "Select" },
        { nameof(SignedOperation.Min), "+", "+", "Select +" },
        { nameof(SignedOperation.Min), "+", "-", "second -" },
        { nameof(SignedOperation.Min), "+", "d", "Select" },
        { nameof(SignedOperation.Min), "-", "+", "first -" },
        { nameof(SignedOperation.Min), "-", "-", "Select -" },
        { nameof(SignedOperation.Min), "-", "d", "Select" },
        { nameof(SignedOperation.Min), "d", "+", "Select" },
        { nameof(SignedOperation.Min), "d", "-", "Select" },
        { nameof(SignedOperation.Min), "d", "d", "Select" },
        // mutant: PlanNegate without `!`
        { nameof(SignedOperation.Negate), "+", "+", "-" },
        { nameof(SignedOperation.Negate), "-", "+", "+" },
        { nameof(SignedOperation.Negate), "d", "+", "Xor" },
        // mutant: PlanNot planned on the host for a negative value (it misses -0)
        { nameof(SignedOperation.Not), "+", "+", "-" },
        { nameof(SignedOperation.Not), "-", "+", "Not" },
        { nameof(SignedOperation.Not), "d", "+", "Not" },
        // mutant: PlanFit with Xor instead of Forward (a launch that is not needed)
        { nameof(SignedOperation.Fit), "+", "+", "+" },
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

        // Expected: an optional chosen operand ("first", "second"), an optional kernel and an optional host sign
        var chosen = ChosenOperand.None;
        var kernel = SignKernel.None;
        string? sign = null;
        foreach (var token in expected.Split(' '))
        {
            if (token is "first" or "second")
            {
                chosen = token == "first" ? ChosenOperand.First : ChosenOperand.Second;
            }
            else if (token is "+" or "-")
            {
                sign = token;
            }
            else
            {
                kernel = Enum.Parse<SignKernel>(token);
            }
        }

        var context = $"{operation} {first} {second}: {plan}";
        Assert.True(chosen == plan.Chosen, context);
        Assert.True(kernel == plan.Kernel, context);
        Assert.True((sign is not null) == plan.HostSign, context);
        if (sign is not null)
        {
            Assert.True((sign == "-") == plan.HostNegative, context);
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
