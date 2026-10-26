using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// A user struct whose constructor arguments are not its fields in order. Sum is not symmetric in a and b, so swapped
// arguments show.
public struct VcScaled
{
    public int Sum;
    public int Scale;

    public VcScaled(int a, int b)
    {
        Sum = a * 10 + b;
        Scale = 2;
    }
}

// Three ints in a row, which a Span<int> over the address of A reaches
public struct VcTriple
{
    public int A;
    public int B;
    public int C;
}

// The System.Numerics.Vector constructors whose parameters are not the components in order (#499). Each kernel writes
// the components of the vectors it builds, X first.
public static partial class VectorConstructorKernels
{
    [GpuKernel]
    public static void SplatKernel(float[] output, float value)
    {
        var v2 = new System.Numerics.Vector2(value);
        var v3 = new System.Numerics.Vector3(value + 1f);
        var v4 = new System.Numerics.Vector4(value + 2f);
        output[0] = v2.X;
        output[1] = v2.Y;
        output[2] = v3.X;
        output[3] = v3.Y;
        output[4] = v3.Z;
        output[5] = v4.X;
        output[6] = v4.Y;
        output[7] = v4.Z;
        output[8] = v4.W;
    }

    [GpuKernel]
    public static void ExtendKernel(float[] output)
    {
        var xy = new System.Numerics.Vector2(1f, 2f);
        var v3 = new System.Numerics.Vector3(xy, 3f);
        var v4 = new System.Numerics.Vector4(v3, 4f);
        var w4 = new System.Numerics.Vector4(xy, 5f, 6f);
        output[0] = v3.X;
        output[1] = v3.Y;
        output[2] = v3.Z;
        output[3] = v4.X;
        output[4] = v4.Y;
        output[5] = v4.Z;
        output[6] = v4.W;
        output[7] = w4.X;
        output[8] = w4.Y;
        output[9] = w4.Z;
        output[10] = w4.W;
    }

    [GpuKernel]
    public static void TargetTypedSplatKernel(float[] output)
    {
        System.Numerics.Vector3 v = new(7f);
        output[0] = v.X;
        output[1] = v.Y;
        output[2] = v.Z;
    }

    // The argument is evaluated once: the splat reads the counter after a single increment
    [GpuKernel]
    public static void SplatEvaluatesOnceKernel(float[] output)
    {
        float counter = 0f;
        var v = new System.Numerics.Vector3(++counter);
        output[0] = v.X;
        output[1] = v.Y;
        output[2] = v.Z;
        output[3] = counter;
    }

    // The positional constructors keep their meaning
    [GpuKernel]
    public static void PositionalKernel(float[] output)
    {
        var v4 = new System.Numerics.Vector4(1f, 2f, 3f, 4f);
        var f3 = new Float3(5f, 6f, 7f);
        output[0] = v4.X;
        output[1] = v4.Y;
        output[2] = v4.Z;
        output[3] = v4.W;
        output[4] = f3.X;
        output[5] = f3.Y;
        output[6] = f3.Z;
    }

    // A user struct constructor through target-typed new() reaches its factory function
    [GpuKernel]
    public static void TargetTypedUserConstructorKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        VcScaled s = new(tid, 5);
        output[tid] = s.Sum * 100 + s.Scale;
    }

    // Named arguments in another order than the parameters, on each way of building a struct: aggregate
    // initialization (Vector3, Core Float3), a mapped function (Vector4(Vector3, float)) and a factory function
    [GpuKernel]
    public static void NamedArgumentsKernel(float[] output)
    {
        var v3 = new System.Numerics.Vector3(z: 3f, x: 1f, y: 2f);
        var f3 = new Float3(z: 6f, x: 4f, y: 5f);
        var v4 = new System.Numerics.Vector4(w: 10f, value: new System.Numerics.Vector3(y: 8f, z: 9f, x: 7f));
        var scaled = new VcScaled(b: 1, a: 20);
        output[0] = v3.X;
        output[1] = v3.Y;
        output[2] = v3.Z;
        output[3] = f3.X;
        output[4] = f3.Y;
        output[5] = f3.Z;
        output[6] = v4.X;
        output[7] = v4.Y;
        output[8] = v4.Z;
        output[9] = v4.W;
        output[10] = scaled.Sum;
    }

    // C# evaluates the arguments in the written order (z, x, y), then passes them in the parameter order
    [GpuKernel]
    public static void NamedArgumentsEvaluationOrderKernel(float[] buffer, float[] output)
    {
        int i = 0;
        var v = new System.Numerics.Vector3(z: buffer[i++], x: buffer[i++], y: buffer[i++]);
        output[0] = v.X;
        output[1] = v.Y;
        output[2] = v.Z;
        output[3] = i;
    }

    // Span<int>(void* pointer, int length) with the length written first
    [GpuKernel]
    public static void NamedSpanArgumentsKernel(int[] output)
    {
        unsafe
        {
            VcTriple t = default;
            var span = new System.Span<int>(length: 3, pointer: &t.A);
            span[0] = 7;
            span[1] = 8;
            span[2] = 9;
            output[0] = t.A;
            output[1] = t.B;
            output[2] = t.C;
            output[3] = span.Length;
        }
    }

    // The arguments of a factory function (VcScaled) and of a mapped function (Vector4(Vector3, float)) written in
    // another order than the parameters: C# evaluates them in the written order
    [GpuKernel]
    public static void NamedCallArgumentsEvaluationOrderKernel(float[] buffer, float[] output)
    {
        int i = 0;
        var scaled = new VcScaled(b: (int)buffer[i++], a: (int)buffer[i++]);
        var v4 = new System.Numerics.Vector4(w: buffer[i++], value: new System.Numerics.Vector3(buffer[i++]));
        output[0] = scaled.Sum;
        output[1] = v4.X;
        output[2] = v4.Y;
        output[3] = v4.Z;
        output[4] = v4.W;
        output[5] = i;
    }
}
