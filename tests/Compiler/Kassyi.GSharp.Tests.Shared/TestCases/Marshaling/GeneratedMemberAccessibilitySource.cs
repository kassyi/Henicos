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

// The shapes of a C# oracle (a small project that put each keyword on a member of the container and named the type):
// which keywords the compiler accepts depends on the domain of the type and of the container, protected domains
// included, which also reach the types derived from the type that declares them

// A static class that is protected in its outer class names a protected type of that outer class: public is accepted
public class ProtectedContainer
{
    protected struct S
    {
    }

    protected static class StaticK
    {
    }

    public static class PublicStaticK
    {
    }

    public class InstanceK
    {
        protected struct Own
        {
        }
    }
}

// A derived class names a protected type of its base
public class ProtectedBase
{
    protected struct S
    {
    }

    protected internal struct SInternal
    {
    }

    private protected struct SPrivateProtected
    {
    }
}

public class ProtectedDerived : ProtectedBase
{
}

public static class StaticDerivedHost
{
}

public sealed class SealedDerived : ProtectedBase
{
}

public class FamilyOrAssemblyHost
{
    protected internal struct S
    {
    }

    public class InstanceK
    {
    }
}

public class FamilyAndAssemblyHost
{
    private protected struct S
    {
    }

    public class InstanceK
    {
    }
}
