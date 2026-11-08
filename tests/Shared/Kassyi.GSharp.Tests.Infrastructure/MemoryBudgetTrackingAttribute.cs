#nullable enable
using System.Reflection;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Reports each test to the memory budget watchdog (<see cref="MemoryBudget"/>) and fails the tests that were running
/// when the test host exceeded its budget. <c>tests/Directory.Build.props</c> applies it to every test assembly.
/// </summary>
/// <remarks>
/// xUnit v2 runs the <see cref="BeforeAfterTestAttribute"/>s of the assembly, the class and the method around each test
/// method invocation; an exception from <see cref="After"/> fails the test.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class MemoryBudgetTrackingAttribute : BeforeAfterTestAttribute
{
    /// <inheritdoc/>
    public override void Before(MethodInfo methodUnderTest)
    {
        if (IsStressOrBenchmark(methodUnderTest))
        {
            MemoryBudget.EnterStressProfile();
        }

        MemoryBudget.Current?.TestStarting(TestName(methodUnderTest));
    }

    /// <inheritdoc/>
    public override void After(MethodInfo methodUnderTest)
    {
        if (MemoryBudget.Current?.TestFinished(TestName(methodUnderTest)) is { } failure)
        {
            throw new MemoryBudgetExceededException(failure);
        }
    }

    // [Category(TestCategory.Stress)] or [Category(TestCategory.Benchmark)] on the method or its class
    private static bool IsStressOrBenchmark(MethodInfo method)
    {
        foreach (CategoryAttribute category in method.GetCustomAttributes<CategoryAttribute>())
        {
            if (category.Category is TestCategory.Stress or TestCategory.Benchmark)
            {
                return true;
            }
        }

        if ((method.ReflectedType ?? method.DeclaringType) is not { } type)
        {
            return false;
        }

        foreach (CategoryAttribute category in type.GetCustomAttributes<CategoryAttribute>())
        {
            if (category.Category is TestCategory.Stress or TestCategory.Benchmark)
            {
                return true;
            }
        }

        return false;
    }

    private static string TestName(MethodInfo method) =>
        (method.ReflectedType ?? method.DeclaringType) is { } type ? type.FullName + "." + method.Name : method.Name;
}

/// <summary>
/// The test host exceeded its memory budget while the failing test was running.
/// </summary>
/// <param name="message">The peak, the budget and the tests that were running.</param>
public sealed class MemoryBudgetExceededException(string message) : Exception(message);
