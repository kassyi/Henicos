namespace Access;

public class Open
{
}

internal class Inner
{
}

public class Generic<T>
{
}

// Types of each accessibility, and classes of each accessibility that generated members could be declared in
public class Host
{
    private struct PrivateS
    {
    }

    internal struct InternalS
    {
    }

    protected internal struct ProtectedInternalS
    {
    }

    protected struct ProtectedS
    {
    }

    private protected struct PrivateProtectedS
    {
    }

    private static class Hidden
    {
        public struct Deep
        {
        }
    }

    public static class PublicNested
    {
    }

    internal static class InternalNested
    {
    }

    private static class PrivateNested
    {
        public static class InPrivate
        {
        }
    }

    protected static class ProtectedNested
    {
    }
}
