using Kassyi.GSharp.Tests.DeviceLibrary;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A converted struct whose fields are all private: the launcher copies them through the host mirror of the shadow
/// struct, on every marshaling path (scalar, array, nested struct, struct array, fixed buffer), in both directions.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class PrivateFieldStructTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void StepPrivateFieldsKernel_EveryFieldPrivate_RoundTripsEachField()
    {
        int[] data = [1, 2, 3];
        int[] innerValues = [0];
        int[] firstItemValues = [0];
        int[] secondItemValues = [0];
        var holder = new PfHolder(
            scale: 3,
            data,
            new PfInner(innerValues, bias: 5),
            [new PfInner(firstItemValues, bias: 7), new PfInner(secondItemValues, bias: 11)]);

        PrivateFieldStructKernels.LaunchStepPrivateFieldsKernel(ref holder, blockDimX: 1);

        // Scalar in (_scale = 3) and nested scalar in (_inner._bias = 5) through the private array: data[i] * 3 + 5
        // mutant: NativeStructEmitter.NeedsHostMirror without `|| IsUnnameableField(...)` -> the launcher reads
        // host._scale and the build fails with CS0122
        // mutant: StructMarshalMethodEmitter.WriteMarshalMember `native.<f> = host.<f>;` -> `native.<f> = default;`
        // -> _scale and _bias reach the device as 0 ([0, 0, 0])
        Assert.Equal([8, 11, 14], data);

        // Scalar out: the device negated _scale
        // mutant: StructUnmarshalMethodEmitter.WriteUnmarshalMember writes `;` for `host.<f> = native.<f>;` -> Scale
        // stays 3
        Assert.Equal(-3, holder.Scale);
        Assert.Same(data, holder.Data);

        // Fixed buffer in (first + last element, 100 + 400) through the nested struct's private array, and out
        // (each + 1)
        // mutant: StructBlockCopyEmitter.WriteBlockCopy FixedBuffer copies 0 bytes (`MemoryCopy(..., 0, 0)`) -> the sum
        // is 0
        Assert.Equal(100 + 400, innerValues[0]);
        Assert.Equal(101, holder.First);
        Assert.Equal(401, holder.Last);

        // Nested struct out: _inner._bias + 10
        // mutant: StructUnmarshalMethodEmitter.WriteUnmarshalMember writes `if (false) ` before Unmarshal_<nested> ->
        // Bias stays 5
        Assert.Equal(15, holder.Inner.Bias);

        // Struct array in (7 + 11 into the second element's private array) and out (first element's _bias + 1)
        // mutant: StructMarshalMethodEmitter.WriteCpuMarshalArrayMember writes `if (false) ` before
        // Marshal_<element> -> the elements reach the device without their arrays and the launch throws
        // NullReferenceException
        Assert.Equal(18, secondItemValues[0]);
        Assert.Equal(8, holder.Items[0].Bias);
        Assert.Equal(11, holder.Items[1].Bias);
        Assert.Equal(0, firstItemValues[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ReadExplicitSwappedKernel_PrivateFieldsOutOfOffsetOrder_ReadsEachFieldAtItsOffset()
    {
        var value = new PfExplicitSwapped(a: 1, b: 2, c: 3);
        var output = new int[3];

        PrivateFieldStructKernels.LaunchReadExplicitSwappedKernel(value, output, blockDimX: 1);

        // _a sits at offset 4 and _b at 0, declared in that order: a sequential mirror would swap them on CUDA
        // mutant: HostMirrorPlan.Create `structLayout.Kind switch { TypeLayoutKind.Explicit => "Explicit", ...}` ->
        // "Sequential" with no [FieldOffset] -> FieldOffsetsMatch is false and the launch throws
        Assert.Equal([1, 2, 3], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void StepLibraryInternalFieldsKernel_InternalFieldsOfAnotherAssembly_RoundTripUnchanged()
    {
        int[] data = [41];
        var holder = new LibInternalFieldHolder(data, hidden: 7, wide: 1L << 40);

        PrivateFieldStructKernels.LaunchStepLibraryInternalFieldsKernel(ref holder, blockDimX: 1);

        // The launcher cannot name the internal fields of the library and copies them through the host mirror
        // mutant: StructUnmarshalMethodEmitter.WriteUnmarshalMember writes `;` for `host.<f> = native.<f>;` is not
        // seen here (the values do not change); StructMarshalMethodEmitter.WriteMarshalMember `= default` writes 0 back
        Assert.Equal(42, data[0]);
        Assert.Same(data, holder.Data);
        Assert.Equal(7, holder.HiddenValue);
        Assert.Equal(1L << 40, holder.WideValue);
    }
}
