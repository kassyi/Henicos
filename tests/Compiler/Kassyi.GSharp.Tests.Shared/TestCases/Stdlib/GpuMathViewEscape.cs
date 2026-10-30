// Uses of the GpuMath views (ref struct) that C# rejects; GpuMathViewEscapeTests checks each diagnostic and its span.
// Keep the line layout: the test pins the line and column of every error.
using System.Collections.Generic;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

namespace GpuMathViewEscapeCases;

public class HolderClass
{
    private GpuBigUIntView _view;                                       // RS1: field of a class
}

public struct HolderStruct
{
    private GpuBigUIntView _view;                                       // RS1: field of a non-ref struct
}

public static class Escapes
{
    // RS2 (a view used across await) is reported while the async method is lowered: GpuMathViewEscapeAsync.cs

    public static void Captured(GpuBigUIntArray array)
    {
        var view = array.GetElement(0);
        System.Func<bool> lambda = () => view.IsZero();                 // RS3: captured by a lambda
        bool Local() => view.IsZero();                                  // RS3: captured by a local function
        _ = lambda;
        _ = Local();
    }

    public static void AsInterface(GpuBigUIntArray array)
    {
        System.IEquatable<GpuBigUIntView> equatable = array.GetElement(0);  // RS4b: interface type argument
        _ = equatable;
    }

    public static void AsTypeArgument()
    {
        var list = new List<GpuBigUIntView>();                          // RS5: generic type argument
        _ = list;
    }

    public static void AsArrayElement()
    {
        var views = new GpuBigUIntView[2];                              // RS6: array element
        _ = views;
    }

    public static void Disposed(GpuBigUIntArray array)
    {
        array.GetElement(0).Dispose();                                  // RS7: no Dispose
        using var view = array.GetElement(1);                           // RS7: not disposable
    }

    public static void Operator(GpuBigUIntArray array, GpuBigUInt owned)
    {
        _ = array.GetElement(0) + owned;                                // RS8: no expression operators
    }

    public static void Nullable()
    {
        GpuBigUIntView? target = null;                                  // RS9: Nullable<T>
        _ = target;
    }

    public static void Boxed(GpuBigUIntArray array)
    {
        var slice = array.Slice(0, 1);
        object boxed = slice;                                           // RS4: boxing an array view
        object element = array.GetElement(0);                           // RS4: boxing a view (no implicit proxy)
        _ = boxed;
        _ = element;
    }
}

public class HolderOfResult
{
    private GpuBigIntArrayView _result;                                 // RS11: a vector result in a field of a class

    public void Keep(
        Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions.IGpuBigIntVectorExpression expression,
        Kassyi.GSharp.Stdlib.GpuMath.GpuWorkspace workspace)
    {
        _result = expression.Evaluate(workspace);
    }
}

public static class VectorResultEscapes
{
    public static void Captured(
        Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions.IGpuBigIntVectorExpression expression,
        Kassyi.GSharp.Stdlib.GpuMath.GpuWorkspace workspace)
    {
        var result = expression.Evaluate(workspace);
        System.Func<int> lambda = () => result.Length;                  // RS11: an integer result captured
        _ = lambda;
    }

    public static void Boxed(
        Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions.IGpuBigFloatVectorExpression floats,
        Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions.IGpuBoolVectorExpression mask,
        Kassyi.GSharp.Stdlib.GpuMath.GpuWorkspace workspace)
    {
        object floatResult = floats.Evaluate(workspace);                // RS11: a float result boxed
        object maskResult = mask.Evaluate(workspace);                   // RS11: a mask boxed
        _ = floatResult;
        _ = maskResult;
    }
}
