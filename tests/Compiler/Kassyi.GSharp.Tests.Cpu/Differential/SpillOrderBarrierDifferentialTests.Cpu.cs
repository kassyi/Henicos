using System;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class SpillOrderBarrierDifferentialTests
{
    partial void RunManagedVerification(Action action)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            action();
        }
    }
}
