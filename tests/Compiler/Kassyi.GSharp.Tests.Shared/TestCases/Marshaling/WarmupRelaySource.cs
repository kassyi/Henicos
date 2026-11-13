namespace Relay;

// Kernel classes of each reach from the assembly; the plan tests build the kernels themselves
public static partial class PublicKernels
{
}

public static partial class OneLevel
{
    private static partial class Kernels
    {
    }
}

internal static partial class TwoLevels
{
    private static partial class Middle
    {
        private static partial class Kernels
        {
        }
    }
}

// The innermost container the assembly can name is the middle one
public static partial class Outermost
{
    internal static partial class Reachable
    {
        private static partial class Kernels
        {
        }
    }
}

file static partial class FileLocal
{
    private static partial class Kernels
    {
    }
}
