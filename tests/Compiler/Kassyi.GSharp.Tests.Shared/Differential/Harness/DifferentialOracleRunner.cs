#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Executes the reference or sequential C# oracle of a <see cref="DifferentialCase"/> across thread orders and verifies
/// order independence.
/// </summary>
internal static class DifferentialOracleRunner
{
    public static DiffBuffers? RunOracle(
        DifferentialCase testCase,
        DiffBuffers initial,
        LaunchShape shape,
        int seed,
        out ExceptionPolicy? matchedExceptionPolicy,
        out DiffBuffers? afterException
    )
    {
        matchedExceptionPolicy = null;
        afterException = null;
        var expected = initial.Clone();
        expected.Shape = shape;
        if (testCase.Reference is { } reference)
        {
            Guard(testCase, seed, "reference model", () => reference(expected, shape));
            return expected;
        }

        var oracle = testCase.EffectiveOracle ?? throw new DifferentialHarnessException($"{testCase.Name}: no oracle");
        if (testCase.OracleExceptions == OracleExceptionMode.MatchOracle)
        {
            expected = RunOracleOrders(testCase, oracle, initial, shape, seed, out var thrown);
            if (thrown is not null)
            {
                matchedExceptionPolicy = new ExceptionPolicy(thrown.GetType(), testCase.ExceptionPaths)
                {
                    // C# names the parameter of an argument exception, or none; every path has to do the same
                    ParamName = (thrown as ArgumentException)?.ParamName,
                    ParamNameIsExact = thrown is ArgumentException,
                };
                afterException = expected;
                return null;
            }

            return expected;
        }

        var orders =
            testCase.OracleOrders ?? [ThreadOrder.Forward, ThreadOrder.Reverse, ThreadOrder.Permutation(seed)];
        var firstOrder = orders.Count > 0 ? orders[0] : ThreadOrder.Forward;
        Guard(
            testCase,
            seed,
            $"oracle ({firstOrder})",
            () => SequentialOracle.Run(shape, firstOrder, () => oracle(expected, shape))
        );

        for (int i = 1; i < orders.Count; i++)
        {
            var order = orders[i];
            var other = initial.Clone();
            other.Shape = shape;
            Guard(
                testCase,
                seed,
                $"oracle ({order})",
                () => SequentialOracle.Run(shape, order, () => oracle(other, shape))
            );
            CheckOrderIndependence(testCase, seed, expected, other, order);
        }

        return expected;
    }

    // Runs the oracle in every order of the case, each on its own copy of the inputs, and requires the same outcome in
    // all of them: the same exception type, or none, and the same outputs (an output that depends on the order is a
    // race, also when a thread throws). Returns the outputs of the first order, and in thrown its exception or null
    public static DiffBuffers RunOracleOrders(
        DifferentialCase testCase,
        Action<DiffBuffers, LaunchShape> oracle,
        DiffBuffers initial,
        LaunchShape shape,
        int seed,
        out Exception? thrown
    )
    {
        var orders =
            testCase.OracleOrders ?? [ThreadOrder.Forward, ThreadOrder.Reverse, ThreadOrder.Permutation(seed)];
        var firstOrder = orders.Count > 0 ? orders[0] : ThreadOrder.Forward;
        var expected = initial.Clone();
        expected.Shape = shape;
        var firstThrown = TryRunOrder(shape, firstOrder, () => oracle(expected, shape));
        for (int i = 1; i < orders.Count; i++)
        {
            var order = orders[i];
            var other = initial.Clone();
            other.Shape = shape;
            var otherThrown = TryRunOrder(shape, order, () => oracle(other, shape));
            if (firstThrown?.GetType() != otherThrown?.GetType())
            {
                throw new DifferentialHarnessException(
                    $"{testCase.Name}: oracle exception depends on thread order for seed {seed}: "
                        + $"{firstOrder} {Outcome(firstThrown)}, but {order} {Outcome(otherThrown)}"
                );
            }

            CheckOrderIndependence(testCase, seed, expected, other, order);
        }

        thrown = firstThrown;
        return expected;
    }

    // The outputs of the oracle for a seed whose exception the case states (ExpectedException or ExceptionPolicy): the
    // oracle must throw that exception as well, and the paths must leave its writes
    public static DiffBuffers RunOracleExpecting(
        DifferentialCase testCase,
        in DifferentialRunner.ShapeRun at,
        ExceptionPolicy policy
    )
    {
        var oracle = testCase.EffectiveOracle ?? throw new DifferentialHarnessException($"{testCase.Name}: no oracle");
        var expected = RunOracleOrders(testCase, oracle, at.Initial, at.Shape, at.Seed, out var thrown);
        if (thrown?.GetType() != policy.ExceptionType)
        {
            throw new DifferentialHarnessException(
                $"{testCase.Name}: the case expects {policy} for seed {at.Seed}, but the oracle {Outcome(thrown)}"
            );
        }

        return expected;
    }

    private static string Outcome(Exception? thrown) =>
        thrown is null ? "succeeded" : $"threw {thrown.GetType().Name}";

    private static Exception? TryRunOrder(LaunchShape shape, ThreadOrder order, Action run)
    {
        try
        {
            SequentialOracle.Run(shape, order, run);
            return null;
        }
        catch (Exception ex) when (ex is not DifferentialHarnessException)
        {
            return ExceptionPolicy.Unwrap(ex);
        }
    }

    private static void Guard(DifferentialCase testCase, int seed, string what, Action run)
    {
        try
        {
            run();
        }
        catch (Exception ex) when (ex is not DifferentialHarnessException)
        {
            throw new DifferentialHarnessException(
                $"{testCase.Name}: the {what} threw for seed {seed}; keep inputs that C# rejects out of the generator",
                ex
            );
        }
    }

    private static void CheckOrderIndependence(
        DifferentialCase testCase,
        int seed,
        DiffBuffers forward,
        DiffBuffers other,
        ThreadOrder order
    )
    {
        for (int b = 0; b < forward.All.Count; b++)
        {
            var first = forward.All[b];
            if (first.ColumnPolicies is null &&
                first.Policy.Kind is OutputPolicyKind.Ignore or OutputPolicyKind.Multiset)
            {
                continue;
            }

            CheckBufferOrderIndependence(testCase, seed, first, other.All[b], order);
        }
    }

    private static void CheckBufferOrderIndependence(
        DifferentialCase testCase,
        int seed,
        DiffBuffer first,
        DiffBuffer second,
        ThreadOrder order
    )
    {
        for (int i = 0; i < first.Length; i++)
        {
            if (first.PolicyOf(i).Kind is OutputPolicyKind.Ignore or OutputPolicyKind.Multiset)
            {
                continue;
            }

            // [WHY] a computed floating-point cell (Bitwise) promises only that it is a NaN, not which one: the
            // optimized JIT code may quiet a signaling NaN that the unoptimized code returns as it is. A moved value
            // (Exact) still has to keep every bit
            bool sameValue =
                first.SameBits(second, i) ||
                (first.PolicyOf(i).Kind == OutputPolicyKind.Bitwise && first.CellsMatch(second, i));
            if (!sameValue)
            {
                throw new DifferentialHarnessException(
                    $"{testCase.Name}: kernel output depends on thread order, so the kernel races "
                        + $"(undefined on the GPU) or reads a non-commutative atomic: seed {seed}, "
                        + $"{first.Name}[{i}] column \"{first.ColumnNameOf(i)}\" is {first.FormatCell(i)} "
                        + $"in forward order and {second.FormatCell(i)} in {order} order"
                );
            }
        }
    }
}
