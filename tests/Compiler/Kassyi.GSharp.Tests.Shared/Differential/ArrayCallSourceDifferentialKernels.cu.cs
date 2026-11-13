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
/// Kernels that give a <c>System.Array</c> method an array that is not a kernel argument, one kernel per source: a
/// struct field, an element of a struct array, a local and a device function parameter. Each makes the call
/// <see cref="ArrayCalls"/> names, written out on the source itself so that the array of the call is that source. Every
/// call has length 0, so no path reads an element. <c>o[0]</c> is written before the call and <c>o[1]</c> after it.
/// </summary>
#pragma warning disable CS8604 // C#: ArgumentNullException on a null array, which the tests check
public static partial class ArrayCallSourceDifferentialKernels
{
    [GpuKernel]
    public static void FieldArrayCallKernel(ArrayCallHolder h, int call, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        o[0] = 1;
        switch (call)
        {
            case ArrayCalls.Fill: Array.Fill(h.Data, 5); break;
            case ArrayCalls.FillRange: Array.Fill(h.Data, 5, 0, 0); break;
            case ArrayCalls.Clear: Array.Clear(h.Data); break;
            case ArrayCalls.ClearRange: Array.Clear(h.Data, 0, 0); break;
            case ArrayCalls.Reverse: Array.Reverse(h.Data); break;
            case ArrayCalls.ReverseRange: Array.Reverse(h.Data, 0, 0); break;
            case ArrayCalls.CopyFrom: Array.Copy(h.Data, o, 0); break;
            case ArrayCalls.CopyRangeFrom: Array.Copy(h.Data, 0, o, 0, 0); break;
            case ArrayCalls.CopyTo: Array.Copy(o, h.Data, 0); break;
            case ArrayCalls.CopyRangeTo: Array.Copy(o, 0, h.Data, 0, 0); break;
            case ArrayCalls.FillPastEnd: Array.Fill(h.Data, 5, 1, 0); break;
            case ArrayCalls.ClearPastEnd: Array.Clear(h.Data, 1, 0); break;
            case ArrayCalls.CopyFromPastEnd: Array.Copy(h.Data, 1, o, 0, 0); break;
        }

        o[1] = 2;
    }

    [GpuKernel]
    public static void ElementArrayCallKernel(ArrayCallHolder[] hs, int call, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        o[0] = 1;
        switch (call)
        {
            case ArrayCalls.Fill: Array.Fill(hs[0].Data, 5); break;
            case ArrayCalls.FillRange: Array.Fill(hs[0].Data, 5, 0, 0); break;
            case ArrayCalls.Clear: Array.Clear(hs[0].Data); break;
            case ArrayCalls.ClearRange: Array.Clear(hs[0].Data, 0, 0); break;
            case ArrayCalls.Reverse: Array.Reverse(hs[0].Data); break;
            case ArrayCalls.ReverseRange: Array.Reverse(hs[0].Data, 0, 0); break;
            case ArrayCalls.CopyFrom: Array.Copy(hs[0].Data, o, 0); break;
            case ArrayCalls.CopyRangeFrom: Array.Copy(hs[0].Data, 0, o, 0, 0); break;
            case ArrayCalls.CopyTo: Array.Copy(o, hs[0].Data, 0); break;
            case ArrayCalls.CopyRangeTo: Array.Copy(o, 0, hs[0].Data, 0, 0); break;
            case ArrayCalls.FillPastEnd: Array.Fill(hs[0].Data, 5, 1, 0); break;
            case ArrayCalls.ClearPastEnd: Array.Clear(hs[0].Data, 1, 0); break;
            case ArrayCalls.CopyFromPastEnd: Array.Copy(hs[0].Data, 1, o, 0, 0); break;
        }

        o[1] = 2;
    }

    [GpuKernel]
    public static void LocalArrayCallKernel(int[]? a, int call, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        o[0] = 1;

        // A local assigned null, then the argument's value when it has one
        int[]? local = null;
        if (a is not null)
        {
            local = a;
        }

        switch (call)
        {
            case ArrayCalls.Fill: Array.Fill(local, 5); break;
            case ArrayCalls.FillRange: Array.Fill(local, 5, 0, 0); break;
            case ArrayCalls.Clear: Array.Clear(local); break;
            case ArrayCalls.ClearRange: Array.Clear(local, 0, 0); break;
            case ArrayCalls.Reverse: Array.Reverse(local); break;
            case ArrayCalls.ReverseRange: Array.Reverse(local, 0, 0); break;
            case ArrayCalls.CopyFrom: Array.Copy(local, o, 0); break;
            case ArrayCalls.CopyRangeFrom: Array.Copy(local, 0, o, 0, 0); break;
            case ArrayCalls.CopyTo: Array.Copy(o, local, 0); break;
            case ArrayCalls.CopyRangeTo: Array.Copy(o, 0, local, 0, 0); break;
            case ArrayCalls.FillPastEnd: Array.Fill(local, 5, 1, 0); break;
            case ArrayCalls.ClearPastEnd: Array.Clear(local, 1, 0); break;
            case ArrayCalls.CopyFromPastEnd: Array.Copy(local, 1, o, 0, 0); break;
        }

        o[1] = 2;
    }

    [GpuKernel]
    public static void ParameterArrayCallKernel(int[]? a, int call, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        o[0] = 1;
        CallOnParameter(a, call, o);
        o[1] = 2;
    }

    [GpuDeviceFunction]
    public static void CallOnParameter(int[]? x, int call, int[] o)
    {
        switch (call)
        {
            case ArrayCalls.Fill: Array.Fill(x, 5); break;
            case ArrayCalls.FillRange: Array.Fill(x, 5, 0, 0); break;
            case ArrayCalls.Clear: Array.Clear(x); break;
            case ArrayCalls.ClearRange: Array.Clear(x, 0, 0); break;
            case ArrayCalls.Reverse: Array.Reverse(x); break;
            case ArrayCalls.ReverseRange: Array.Reverse(x, 0, 0); break;
            case ArrayCalls.CopyFrom: Array.Copy(x, o, 0); break;
            case ArrayCalls.CopyRangeFrom: Array.Copy(x, 0, o, 0, 0); break;
            case ArrayCalls.CopyTo: Array.Copy(o, x, 0); break;
            case ArrayCalls.CopyRangeTo: Array.Copy(o, 0, x, 0, 0); break;
            case ArrayCalls.FillPastEnd: Array.Fill(x, 5, 1, 0); break;
            case ArrayCalls.ClearPastEnd: Array.Clear(x, 1, 0); break;
            case ArrayCalls.CopyFromPastEnd: Array.Copy(x, 1, o, 0, 0); break;
        }
    }
}
#pragma warning restore CS8604

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

    /// <summary><c>Array.Fill(array, value, 1, 0)</c>: past the end of an empty array.</summary>
    public const int FillPastEnd = 10;

    /// <summary><c>Array.Clear(array, 1, 0)</c>: past the end of an empty array.</summary>
    public const int ClearPastEnd = 11;

    /// <summary><c>Array.Copy(array, 1, o, 0, 0)</c>: past the end of an empty source.</summary>
    public const int CopyFromPastEnd = 12;

    /// <summary>The number of calls.</summary>
    public const int Count = 13;
}
