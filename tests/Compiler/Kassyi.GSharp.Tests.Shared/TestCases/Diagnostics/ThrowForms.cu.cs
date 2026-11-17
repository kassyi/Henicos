using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0003 for a throw that cannot be carried: an exception that is not created in the throw, a message that is not a
// constant, an exception type the host cannot build
public sealed class IntOnlyThrowException : Exception
{
    public IntOnlyThrowException(int code)
        : base("code")
    {
    }
}

public static partial class ThrowFormsKernels
{
    private static readonly string s_text = "not a constant";

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ThrowVariableKernel(int[] r, int mode)
    {
        var ex = new InvalidOperationException("kept");
        if (mode == 1)
        {
            throw ex;
        }

        r[0] = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void RethrowKernel(int[] r)
    {
        try
        {
            r[0] = 1;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NonConstantMessageKernel(int[] r)
    {
        if (r[0] == 1)
        {
            throw new InvalidOperationException(s_text);
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void UnbuildableTypeKernel(int[] r)
    {
        if (r[0] == 1)
        {
            throw new IntOnlyThrowException(3);
        }
    }
}
