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
    public override void Before(MethodInfo methodUnderTest) =>
        MemoryBudget.Current?.TestStarting(TestName(methodUnderTest));

    /// <inheritdoc/>
    public override void After(MethodInfo methodUnderTest)
    {
        if (MemoryBudget.Current?.TestFinished(TestName(methodUnderTest)) is { } failure)
        {
            throw new MemoryBudgetExceededException(failure);
        }
    }

    private static string TestName(MethodInfo method) =>
        (method.ReflectedType ?? method.DeclaringType) is { } type ? type.FullName + "." + method.Name : method.Name;
}

/// <summary>
/// The test host exceeded its memory budget while the failing test was running.
/// </summary>
/// <param name="message">The peak, the budget and the tests that were running.</param>
public sealed class MemoryBudgetExceededException(string message) : Exception(message);
