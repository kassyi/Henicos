#nullable enable
using System;
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.Shared.Differential;

// Generic and concrete delegates matching KernelHostNames.LaunchOptions (exact 9-param optional tail).
public delegate void DiffLaunch<in T1>(
    T1 arg1,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void DiffLaunch<in T1, in T2>(
    T1 arg1, T2 arg2,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void DiffLaunch<in T1, in T2, in T3>(
    T1 arg1, T2 arg2, T3 arg3,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void DiffLaunch<in T1, in T2, in T3, in T4>(
    T1 arg1, T2 arg2, T3 arg3, T4 arg4,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void Int32Launch(
    int[] a,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void Int32PairLaunch(
    int[] a, int[] dst,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void Int32TripletLaunch(
    int[] a, int[] b, int[] dst,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void Int32RowsLaunch(
    int[] a, int[] dst, int rows,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void Int32PairRowsLaunch(
    int[] a, int[] b, int[] dst, int rows,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void Int32ScalarLaunch(
    int[] a, int rows,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void Int32ToInt64Launch(
    int[] a, long[] dst,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void Int64RowsLaunch(
    long[] a, long[] dst, int rows,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void Int64PairRowsLaunch(
    long[] a, long[] b, long[] dst, int rows,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void Int64ScalarLaunch(
    long[] a, int rows,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void Int32NullablePairLaunch(
    int[] a, int[]? b,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void SinglePairLaunch(
    float[] a, float[] dst,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void SingleRowsLaunch(
    float[] a, float[] dst, int rows,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void UInt32TripletLaunch(
    uint[] a, uint[] b, uint[] dst,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void UInt32TripletRowsLaunch(
    uint[] a, uint[] b, uint[] dst, int rows,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void UInt64TripletRowsLaunch(
    ulong[] a, ulong[] b, ulong[] dst, int rows,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

public delegate void Int32QuadLaunch(
    int[] a, int[] b, int[] c, int[] dst,
    uint gridDimX, uint gridDimY, uint gridDimZ,
    uint blockDimX, uint blockDimY, uint blockDimZ,
    uint sharedMemBytes, IntPtr hStream, ReadOnlySpan<KeyValuePair<string, string>> macros);

/// <summary>
/// Adapts typed generated kernel <c>Launch*</c> method groups into <see cref="Action{DiffBuffers, LaunchShape}"/>
/// delegates for <see cref="DifferentialCase"/>.
/// </summary>
internal static class DiffLaunches
{
    public static Action<DiffBuffers, LaunchShape> Bind(
        Int32Launch launch, string bufferName = "a", uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<int>(bufferName),
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        Int32PairLaunch launch, string inputName = "a", string outputName = "dst", uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<int>(inputName), b.Get<int>(outputName),
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        Int32TripletLaunch launch,
        string firstInputName = "a", string secondInputName = "b", string outputName = "dst",
        uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<int>(firstInputName), b.Get<int>(secondInputName), b.Get<int>(outputName),
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        Int32RowsLaunch launch, string inputName = "a", string outputName = "dst", uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<int>(inputName), b.Get<int>(outputName), s.Rows,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        Int32PairRowsLaunch launch,
        string firstInputName = "a", string secondInputName = "b", string outputName = "dst",
        uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<int>(firstInputName), b.Get<int>(secondInputName), b.Get<int>(outputName), s.Rows,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        Int32ScalarLaunch launch, string bufferName = "a", uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<int>(bufferName), s.Rows,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        Int32ToInt64Launch launch, string inputName = "a", string outputName = "dst", uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<int>(inputName), b.Get<long>(outputName),
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        Int64RowsLaunch launch, string inputName = "a", string outputName = "dst", uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<long>(inputName), b.Get<long>(outputName), s.Rows,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        Int64PairRowsLaunch launch,
        string firstInputName = "a", string secondInputName = "b", string outputName = "dst",
        uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<long>(firstInputName), b.Get<long>(secondInputName), b.Get<long>(outputName), s.Rows,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        Int64ScalarLaunch launch, string bufferName = "a", uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<long>(bufferName), s.Rows,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    /// <summary>
    /// Binds an <see cref="Int32NullablePairLaunch"/> kernel (<c>(int[], int[]?)</c>). Named distinctly from
    /// <see cref="Bind(Int32PairLaunch, string, string, uint)"/> because C# erases nullability during method-group overload resolution.
    /// </summary>
    public static Action<DiffBuffers, LaunchShape> BindNullablePair(
        Int32NullablePairLaunch launch, string firstName = "o", string nullableSecondName = "a", uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<int>(firstName), b.GetNullable<int>(nullableSecondName),
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        SinglePairLaunch launch, string inputName = "a", string outputName = "dst", uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<float>(inputName), b.Get<float>(outputName),
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        SingleRowsLaunch launch, string inputName = "a", string outputName = "dst", uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<float>(inputName), b.Get<float>(outputName), s.Rows,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        UInt32TripletLaunch launch,
        string firstInputName = "a", string secondInputName = "b", string outputName = "dst",
        uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<uint>(firstInputName), b.Get<uint>(secondInputName), b.Get<uint>(outputName),
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        UInt32TripletRowsLaunch launch,
        string firstInputName = "a", string secondInputName = "b", string outputName = "dst",
        uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<uint>(firstInputName), b.Get<uint>(secondInputName), b.Get<uint>(outputName), s.Rows,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        UInt64TripletRowsLaunch launch,
        string firstInputName = "a", string secondInputName = "b", string outputName = "dst",
        uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<ulong>(firstInputName), b.Get<ulong>(secondInputName), b.Get<ulong>(outputName), s.Rows,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    public static Action<DiffBuffers, LaunchShape> Bind(
        Int32QuadLaunch launch,
        string firstName = "a", string secondName = "b", string thirdName = "c", string fourthName = "dst",
        uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<int>(firstName), b.Get<int>(secondName), b.Get<int>(thirdName), b.Get<int>(fourthName),
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    /// <summary>
    /// Binds a generic 2-buffer <c>Launch*</c> method group. Explicit type arguments are required when passing a method group.
    /// </summary>
    public static Action<DiffBuffers, LaunchShape> Bind<TSource, TResult>(
        DiffLaunch<TSource[], TResult[]> launch,
        string inputName = "a", string outputName = "dst", uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<TSource>(inputName), b.Get<TResult>(outputName),
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    /// <summary>
    /// Binds a generic 2-buffer row-counted <c>Launch*</c> method group. Explicit type arguments are required when passing a method group.
    /// </summary>
    public static Action<DiffBuffers, LaunchShape> BindRows<TSource, TResult>(
        DiffLaunch<TSource[], TResult[], int> launch,
        string inputName = "a", string outputName = "dst", uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<TSource>(inputName), b.Get<TResult>(outputName), s.Rows,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);

    /// <summary>
    /// Binds a generic 3-buffer row-counted <c>Launch*</c> method group. Explicit type arguments are required when passing a method group.
    /// </summary>
    public static Action<DiffBuffers, LaunchShape> BindRows<TFirst, TSecond, TResult>(
        DiffLaunch<TFirst[], TSecond[], TResult[], int> launch,
        string firstInputName = "a", string secondInputName = "b", string outputName = "dst",
        uint sharedMemBytes = 0) =>
        (b, s) => launch(
            b.Get<TFirst>(firstInputName), b.Get<TSecond>(secondInputName), b.Get<TResult>(outputName), s.Rows,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, sharedMemBytes, default, default);
}
