using System.ComponentModel;

namespace Kassyi.GSharp.Core;

public static partial class ManagedGpuContext
{
    /// <summary>
    /// Binds the receiver of a reduced extension method group to its managed implementation at delegate creation time.
    /// </summary>
    /// <typeparam name="TFirst">The type of the extension method receiver.</typeparam>
    /// <typeparam name="TResult">The return type of the extension method.</typeparam>
    /// <param name="first">The receiver value evaluated when the delegate is created.</param>
    /// <param name="method">The static managed implementation of the extension method.</param>
    /// <returns>A delegate with <paramref name="first"/> bound as its first argument.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Func<TResult> __gsharp_bind<TFirst, TResult>(
        TFirst first,
        Func<TFirst, TResult> method) =>
        () => method(first);

    /// <summary>
    /// Binds the receiver of a reduced extension method group to its managed implementation at delegate creation time.
    /// </summary>
    /// <typeparam name="TFirst">The type of the extension method receiver.</typeparam>
    /// <typeparam name="T1">The type of the first remaining parameter.</typeparam>
    /// <typeparam name="TResult">The return type of the extension method.</typeparam>
    /// <param name="first">The receiver value evaluated when the delegate is created.</param>
    /// <param name="method">The static managed implementation of the extension method.</param>
    /// <returns>A delegate with <paramref name="first"/> bound as its first argument.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Func<T1, TResult> __gsharp_bind<TFirst, T1, TResult>(
        TFirst first,
        Func<TFirst, T1, TResult> method) =>
        a1 => method(first, a1);

    /// <summary>
    /// Binds the receiver of a reduced extension method group to its managed implementation at delegate creation time.
    /// </summary>
    /// <typeparam name="TFirst">The type of the extension method receiver.</typeparam>
    /// <typeparam name="T1">The type of the first remaining parameter.</typeparam>
    /// <typeparam name="T2">The type of the second remaining parameter.</typeparam>
    /// <typeparam name="TResult">The return type of the extension method.</typeparam>
    /// <param name="first">The receiver value evaluated when the delegate is created.</param>
    /// <param name="method">The static managed implementation of the extension method.</param>
    /// <returns>A delegate with <paramref name="first"/> bound as its first argument.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Func<T1, T2, TResult> __gsharp_bind<TFirst, T1, T2, TResult>(
        TFirst first,
        Func<TFirst, T1, T2, TResult> method) =>
        (a1, a2) => method(first, a1, a2);

    /// <summary>
    /// Binds the receiver of a reduced extension method group to its managed implementation at delegate creation time.
    /// </summary>
    /// <typeparam name="TFirst">The type of the extension method receiver.</typeparam>
    /// <typeparam name="T1">The type of the first remaining parameter.</typeparam>
    /// <typeparam name="T2">The type of the second remaining parameter.</typeparam>
    /// <typeparam name="T3">The type of the third remaining parameter.</typeparam>
    /// <typeparam name="TResult">The return type of the extension method.</typeparam>
    /// <param name="first">The receiver value evaluated when the delegate is created.</param>
    /// <param name="method">The static managed implementation of the extension method.</param>
    /// <returns>A delegate with <paramref name="first"/> bound as its first argument.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Func<T1, T2, T3, TResult> __gsharp_bind<TFirst, T1, T2, T3, TResult>(
        TFirst first,
        Func<TFirst, T1, T2, T3, TResult> method) =>
        (a1, a2, a3) => method(first, a1, a2, a3);

    /// <summary>
    /// Binds the receiver of a reduced extension method group to its managed implementation at delegate creation time.
    /// </summary>
    /// <typeparam name="TFirst">The type of the extension method receiver.</typeparam>
    /// <typeparam name="T1">The type of the first remaining parameter.</typeparam>
    /// <typeparam name="T2">The type of the second remaining parameter.</typeparam>
    /// <typeparam name="T3">The type of the third remaining parameter.</typeparam>
    /// <typeparam name="T4">The type of the fourth remaining parameter.</typeparam>
    /// <typeparam name="TResult">The return type of the extension method.</typeparam>
    /// <param name="first">The receiver value evaluated when the delegate is created.</param>
    /// <param name="method">The static managed implementation of the extension method.</param>
    /// <returns>A delegate with <paramref name="first"/> bound as its first argument.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Func<T1, T2, T3, T4, TResult> __gsharp_bind<TFirst, T1, T2, T3, T4, TResult>(
        TFirst first,
        Func<TFirst, T1, T2, T3, T4, TResult> method) =>
        (a1, a2, a3, a4) => method(first, a1, a2, a3, a4);

    /// <summary>
    /// Binds the receiver of a reduced extension method group to its managed implementation at delegate creation time.
    /// </summary>
    /// <typeparam name="TFirst">The type of the extension method receiver.</typeparam>
    /// <param name="first">The receiver value evaluated when the delegate is created.</param>
    /// <param name="method">The static managed implementation of the extension method.</param>
    /// <returns>A delegate with <paramref name="first"/> bound as its first argument.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Action __gsharp_bind<TFirst>(
        TFirst first,
        Action<TFirst> method) =>
        () => method(first);

    /// <summary>
    /// Binds the receiver of a reduced extension method group to its managed implementation at delegate creation time.
    /// </summary>
    /// <typeparam name="TFirst">The type of the extension method receiver.</typeparam>
    /// <typeparam name="T1">The type of the first remaining parameter.</typeparam>
    /// <param name="first">The receiver value evaluated when the delegate is created.</param>
    /// <param name="method">The static managed implementation of the extension method.</param>
    /// <returns>A delegate with <paramref name="first"/> bound as its first argument.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Action<T1> __gsharp_bind<TFirst, T1>(
        TFirst first,
        Action<TFirst, T1> method) =>
        a1 => method(first, a1);

    /// <summary>
    /// Binds the receiver of a reduced extension method group to its managed implementation at delegate creation time.
    /// </summary>
    /// <typeparam name="TFirst">The type of the extension method receiver.</typeparam>
    /// <typeparam name="T1">The type of the first remaining parameter.</typeparam>
    /// <typeparam name="T2">The type of the second remaining parameter.</typeparam>
    /// <param name="first">The receiver value evaluated when the delegate is created.</param>
    /// <param name="method">The static managed implementation of the extension method.</param>
    /// <returns>A delegate with <paramref name="first"/> bound as its first argument.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Action<T1, T2> __gsharp_bind<TFirst, T1, T2>(
        TFirst first,
        Action<TFirst, T1, T2> method) =>
        (a1, a2) => method(first, a1, a2);

    /// <summary>
    /// Binds the receiver of a reduced extension method group to its managed implementation at delegate creation time.
    /// </summary>
    /// <typeparam name="TFirst">The type of the extension method receiver.</typeparam>
    /// <typeparam name="T1">The type of the first remaining parameter.</typeparam>
    /// <typeparam name="T2">The type of the second remaining parameter.</typeparam>
    /// <typeparam name="T3">The type of the third remaining parameter.</typeparam>
    /// <param name="first">The receiver value evaluated when the delegate is created.</param>
    /// <param name="method">The static managed implementation of the extension method.</param>
    /// <returns>A delegate with <paramref name="first"/> bound as its first argument.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Action<T1, T2, T3> __gsharp_bind<TFirst, T1, T2, T3>(
        TFirst first,
        Action<TFirst, T1, T2, T3> method) =>
        (a1, a2, a3) => method(first, a1, a2, a3);

    /// <summary>
    /// Binds the receiver of a reduced extension method group to its managed implementation at delegate creation time.
    /// </summary>
    /// <typeparam name="TFirst">The type of the extension method receiver.</typeparam>
    /// <typeparam name="T1">The type of the first remaining parameter.</typeparam>
    /// <typeparam name="T2">The type of the second remaining parameter.</typeparam>
    /// <typeparam name="T3">The type of the third remaining parameter.</typeparam>
    /// <typeparam name="T4">The type of the fourth remaining parameter.</typeparam>
    /// <param name="first">The receiver value evaluated when the delegate is created.</param>
    /// <param name="method">The static managed implementation of the extension method.</param>
    /// <returns>A delegate with <paramref name="first"/> bound as its first argument.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Action<T1, T2, T3, T4> __gsharp_bind<TFirst, T1, T2, T3, T4>(
        TFirst first,
        Action<TFirst, T1, T2, T3, T4> method) =>
        (a1, a2, a3, a4) => method(first, a1, a2, a3, a4);
}
