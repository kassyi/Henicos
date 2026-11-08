#nullable enable
using System.Globalization;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests.Infrastructure.Tests;

/// <summary>
/// The fixture of <see cref="MemoryBudgetBreachTests"/>: allocates and touches native memory, 16 MB in a normal run and
/// the size of <see cref="AllocationVariable"/> when that test runs this assembly as a child process.
/// </summary>
public class MemoryBudgetFixtureTests
{
    /// <summary>
    /// The environment variable with the size to allocate, in MB.
    /// </summary>
    public const string AllocationVariable = "GSHARP_MEMORY_FIXTURE_MB";

    private const int DefaultMegabytes = 16;
    private const int PageSize = 4096;

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void AllocatesPastBudget()
    {
        string? text = Environment.GetEnvironmentVariable(AllocationVariable);
        bool parsed = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value);
        int megabytes = parsed ? value : DefaultMegabytes;
        long bytes = (long)megabytes * 1024 * 1024;

        long touched = 0;
        IntPtr block = Marshal.AllocHGlobal((nint)bytes);
        try
        {
            // Native and touched, so the allocation counts on Windows (commit) and on Linux (resident pages) alike
            for (long offset = 0; offset < bytes; offset += PageSize)
            {
                Marshal.WriteByte((nint)block + (nint)offset, 1);
                touched += PageSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(block);
        }

        Assert.Equal(bytes, touched);
    }
}
