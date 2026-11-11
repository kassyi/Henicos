using System;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Base of the span and array member tests that run each kernel on the native path and, in the CPU test assembly,
/// on the managed path.
/// </summary>
public abstract class SpanKernelPathTestBase : TranspilerTestBase
{
    protected void Run(bool managed, Action launch)
    {
        Skip.If(managed && IsCudaDialect, "The managed path runs on the CPU test assembly.");
#if GSHARP_CPU
        if (managed)
        {
            ManagedKernelRun.Run(launch);
            return;
        }
#endif
        launch();
    }
}
