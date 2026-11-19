namespace Kassyi.GSharp.Compiler.Helpers;

internal static class EquatableArrayExtensions
{
    public static EquatableArray<T> AsEquatableArray<T>(this ImmutableArray<T> array) =>
        new(array);
}
