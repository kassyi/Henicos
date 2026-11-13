#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A struct argument whose array field a caller may leave null or set to an empty array.</summary>
public struct ArrayCallHolder
{
    public int[]? Data;
    public int Tag;
}

/// <summary>
/// Kernels that give a <c>System.Array</c> method an array that is not a kernel argument: a struct field, a local or a
/// device function parameter (<see cref="ArrayCallSources"/>), to the call <see cref="ArrayCalls"/> names. Every call
/// has length 0, so no range check fails first and no element is read. <c>o[0]</c> is written before the call and
/// <c>o[1]</c> after it.
/// </summary>
#pragma warning disable CS8604 // C#: ArgumentNullException on a null array, which the tests check
public static partial class ArrayCallSourceDifferentialKernels
{
    [GpuKernel]
    public static void ArrayCallKernel(ArrayCallHolder h, int[]? a, int source, int call, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        o[0] = 1;
        if (source == ArrayCallSources.Field)
        {
            switch (call)
            {
                case ArrayCalls.Fill:
                    Array.Fill(h.Data, 5);
                    break;
                case ArrayCalls.FillRange:
                    Array.Fill(h.Data, 5, 0, 0);
                    break;
                case ArrayCalls.Clear:
                    Array.Clear(h.Data);
                    break;
                case ArrayCalls.ClearRange:
                    Array.Clear(h.Data, 0, 0);
                    break;
                case ArrayCalls.Reverse:
                    Array.Reverse(h.Data);
                    break;
                case ArrayCalls.ReverseRange:
                    Array.Reverse(h.Data, 0, 0);
                    break;
                case ArrayCalls.CopyFrom:
                    Array.Copy(h.Data, o, 0);
                    break;
                case ArrayCalls.CopyRangeFrom:
                    Array.Copy(h.Data, 0, o, 0, 0);
                    break;
                case ArrayCalls.CopyTo:
                    Array.Copy(o, h.Data, 0);
                    break;
                case ArrayCalls.CopyRangeTo:
                    Array.Copy(o, 0, h.Data, 0, 0);
                    break;
            }
        }
        else if (source == ArrayCallSources.Local)
        {
            // A local assigned null, then the argument's value when it has one
            int[]? local = null;
            if (a is not null)
            {
                local = a;
            }

            switch (call)
            {
                case ArrayCalls.Fill:
                    Array.Fill(local, 5);
                    break;
                case ArrayCalls.FillRange:
                    Array.Fill(local, 5, 0, 0);
                    break;
                case ArrayCalls.Clear:
                    Array.Clear(local);
                    break;
                case ArrayCalls.ClearRange:
                    Array.Clear(local, 0, 0);
                    break;
                case ArrayCalls.Reverse:
                    Array.Reverse(local);
                    break;
                case ArrayCalls.ReverseRange:
                    Array.Reverse(local, 0, 0);
                    break;
                case ArrayCalls.CopyFrom:
                    Array.Copy(local, o, 0);
                    break;
                case ArrayCalls.CopyRangeFrom:
                    Array.Copy(local, 0, o, 0, 0);
                    break;
                case ArrayCalls.CopyTo:
                    Array.Copy(o, local, 0);
                    break;
                case ArrayCalls.CopyRangeTo:
                    Array.Copy(o, 0, local, 0, 0);
                    break;
            }
        }
        else
        {
            CallOnParameter(a, call, o);
        }

        o[1] = 2;
    }

    [GpuDeviceFunction]
    public static void CallOnParameter(int[]? x, int call, int[] o)
    {
        switch (call)
        {
            case ArrayCalls.Fill:
                Array.Fill(x, 5);
                break;
            case ArrayCalls.FillRange:
                Array.Fill(x, 5, 0, 0);
                break;
            case ArrayCalls.Clear:
                Array.Clear(x);
                break;
            case ArrayCalls.ClearRange:
                Array.Clear(x, 0, 0);
                break;
            case ArrayCalls.Reverse:
                Array.Reverse(x);
                break;
            case ArrayCalls.ReverseRange:
                Array.Reverse(x, 0, 0);
                break;
            case ArrayCalls.CopyFrom:
                Array.Copy(x, o, 0);
                break;
            case ArrayCalls.CopyRangeFrom:
                Array.Copy(x, 0, o, 0, 0);
                break;
            case ArrayCalls.CopyTo:
                Array.Copy(o, x, 0);
                break;
            case ArrayCalls.CopyRangeTo:
                Array.Copy(o, 0, x, 0, 0);
                break;
        }
    }
}
#pragma warning restore CS8604

/// <summary>Where the array of a kernel of <see cref="ArrayCallSourceDifferentialKernels"/> comes from.</summary>
public static class ArrayCallSources
{
    /// <summary>The array field of the struct argument.</summary>
    public const int Field = 0;

    /// <summary>A local that holds null or the array argument.</summary>
    public const int Local = 1;

    /// <summary>The parameter of a device function that the array argument is passed to.</summary>
    public const int Parameter = 2;
}

/// <summary>The <c>System.Array</c> call a kernel of <see cref="ArrayCallSourceDifferentialKernels"/> makes.</summary>
public static class ArrayCalls
{
    /// <summary><c>Array.Fill(array, value)</c>.</summary>
    public const int Fill = 0;

    /// <summary><c>Array.Fill(array, value, 0, 0)</c>.</summary>
    public const int FillRange = 1;

    /// <summary><c>Array.Clear(array)</c>.</summary>
    public const int Clear = 2;

    /// <summary><c>Array.Clear(array, 0, 0)</c>.</summary>
    public const int ClearRange = 3;

    /// <summary><c>Array.Reverse(array)</c>.</summary>
    public const int Reverse = 4;

    /// <summary><c>Array.Reverse(array, 0, 0)</c>.</summary>
    public const int ReverseRange = 5;

    /// <summary><c>Array.Copy(array, o, 0)</c>: the array is the source.</summary>
    public const int CopyFrom = 6;

    /// <summary><c>Array.Copy(array, 0, o, 0, 0)</c>: the array is the source.</summary>
    public const int CopyRangeFrom = 7;

    /// <summary><c>Array.Copy(o, array, 0)</c>: the array is the destination.</summary>
    public const int CopyTo = 8;

    /// <summary><c>Array.Copy(o, 0, array, 0, 0)</c>: the array is the destination.</summary>
    public const int CopyRangeTo = 9;

    /// <summary>The number of calls.</summary>
    public const int Count = 10;
}
