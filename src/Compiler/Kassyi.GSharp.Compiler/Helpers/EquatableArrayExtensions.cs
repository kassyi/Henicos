namespace Kassyi.GSharp.Compiler.Helpers;

internal static class EquatableArrayExtensions
{
    public static EquatableArray<T> AsEquatableArray<T>(this ImmutableArray<T> array) where T : IEquatable<T> =>
        new(array);
}
