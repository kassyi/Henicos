namespace UserTypes
{
    // A user's own types that share the BCL names
    public struct Span<T> { }
    public struct ReadOnlySpan<T> { }
}

public static class Locals
{
    public static void Declare(
        System.Span<int> span,
        System.ReadOnlySpan<float> readOnlySpan,
        int[] array,
        UserTypes.Span<int> userSpan,
        UserTypes.ReadOnlySpan<int> userReadOnlySpan)
    {
    }
}
