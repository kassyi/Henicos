using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="VectorConstructorKernels"/> (#499). The expected values are those of
/// the .NET constructors; the native and managed test classes differ only in how a launch runs.
/// </summary>
internal static class VectorConstructorScenarios
{
    public static void SplatSetsEveryComponent(Action<Action> run)
    {
        var output = new float[9];

        run(() => VectorConstructorKernels.LaunchSplatKernel(output, 1f, blockDimX: 1));

        // new VectorN(v) sets every component to v, not only X
        // mutant: Prelude/VectorConstructors.hpp `float3{value, value, value}` -> `float3{value, 0.0f, 0.0f}` -> [.., 2, 0, 0, ..]
        // (the three splat tests fail)
        Assert.Equal([1f, 1f, 2f, 2f, 2f, 3f, 3f, 3f, 3f], output);
    }

    public static void ExtendAppendsComponents(Action<Action> run)
    {
        var output = new float[11];

        run(() => VectorConstructorKernels.LaunchExtendKernel(output, blockDimX: 1));

        // Vector3(Vector2, z), Vector4(Vector3, w) and Vector4(Vector2, z, w) keep the given components in order
        Assert.Equal([1f, 2f, 3f, 1f, 2f, 3f, 4f, 1f, 2f, 5f, 6f], output);
    }

    public static void TargetTypedSplatSetsEveryComponent(Action<Action> run)
    {
        var output = new float[3];

        run(() => VectorConstructorKernels.LaunchTargetTypedSplatKernel(output, blockDimX: 1));

        Assert.Equal([7f, 7f, 7f], output);
    }

    public static void SplatEvaluatesItsArgumentOnce(Action<Action> run)
    {
        var output = new float[4];

        run(() => VectorConstructorKernels.LaunchSplatEvaluatesOnceKernel(output, blockDimX: 1));

        // A splat that copied the argument expression into each component would increment three times
        Assert.Equal([1f, 1f, 1f, 1f], output);
    }

    public static void PositionalConstructorsKeepTheirMeaning(Action<Action> run)
    {
        var output = new float[7];

        run(() => VectorConstructorKernels.LaunchPositionalKernel(output, blockDimX: 1));

        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f, 7f], output);
    }

    public static void TargetTypedUserConstructorRunsItsBody(Action<Action> run)
    {
        const int Count = 4;
        var output = new int[Count];

        run(() => VectorConstructorKernels.LaunchTargetTypedUserConstructorKernel(output, blockDimX: Count));

        for (int i = 0; i < Count; i++)
        {
            Assert.Equal((i * 10 + 5) * 100 + 2, output[i]);
        }
    }

    public static void NamedArgumentsReachTheirParameters(Action<Action> run)
    {
        var output = new float[11];

        run(() => VectorConstructorKernels.LaunchNamedArgumentsKernel(output, blockDimX: 1));

        // Each named argument lands in its parameter, whatever the written order, on every way of building a struct
        // VcScaled(b: 1, a: 20): Sum = a * 10 + b
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 201f], output);
    }

    public static void NamedArgumentsAreEvaluatedInTheWrittenOrder(Action<Action> run)
    {
        float[] buffer = [10f, 20f, 30f];
        var output = new float[4];

        run(() => VectorConstructorKernels.LaunchNamedArgumentsEvaluationOrderKernel(buffer, output, blockDimX: 1));

        // z reads buffer[0], x buffer[1], y buffer[2]: the written order, and each argument once
        // mutant: ObjectCreationHelper.TranspileReorderedBraces declares the temporaries from the last argument -> [20, 10, 30, 3]
        Assert.Equal([20f, 30f, 10f, 3f], output);
    }

    public static void NamedSpanArgumentsReachTheirParameters(Action<Action> run)
    {
        var output = new int[4];

        run(() => VectorConstructorKernels.LaunchNamedSpanArgumentsKernel(output, blockDimX: 1));

        // The span covers A, B and C (length 3), written through the pointer
        Assert.Equal([7, 8, 9, 3], output);
    }

    public static void NamedCallArgumentsAreEvaluatedInTheWrittenOrder(Action<Action> run)
    {
        float[] buffer = [1f, 2f, 3f, 4f];
        var output = new float[6];

        run(() => VectorConstructorKernels.LaunchNamedCallArgumentsEvaluationOrderKernel(buffer, output, blockDimX: 1));

        // b reads buffer[0] and a buffer[1] (Sum = 2 * 10 + 1); w reads buffer[2] and the splat buffer[3]
        // mutant: ObjectCreationHelper.AppendReorderedArguments passes the temporaries of a call in the reverse parameter
        // order -> Vector4(float, float3) does not compile; limited to calls whose parameters have one type -> [12, ...]
        // mutant: TranspileReordered declares the temporaries from the last argument -> [12, 3, 3, 3, 4, 4]
        Assert.Equal([21f, 4f, 4f, 4f, 3f, 4f], output);
    }
}
