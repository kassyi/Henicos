using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class ConditionalCallTests
{
    static partial void RunManagedPathIfSupported(Action check)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            check();
        }
    }
}
