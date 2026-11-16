using Kassyi.GSharp.Compiler.Errors;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// The table of exception types with an error code of their own is read by the compiler (a <c>throw</c> writes the code)
/// and by the runtime (the code builds the type): every code is in it, and every entry builds the type it names, with its
/// default message and its parameter name rule. A <c>throw</c> whose type the host cannot load fails loudly.
/// </summary>
[Category(TestCategory.Runtime)]
public class ThrowableExceptionTableTests
{
    // None is no error; DataRace is an InvalidOperationException that no throw writes; Throw names its type in the error site
    private static readonly string[] s_notThrowable = [nameof(GSharpErrorCode.None), nameof(GSharpErrorCode.DataRace), nameof(GSharpErrorCode.Throw)];

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Table_EveryErrorCode_HasAnEntryUnlessItIsNotWrittenByAThrow()
    {
        foreach (var name in Enum.GetNames<GSharpErrorCode>().Where(n => !s_notThrowable.Contains(n)))
        {
            Assert.True(ThrowableExceptions.TryFindByCodeName(name, out _), $"{name} has no entry in the table");
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Table_EveryEntry_BuildsItsTypeWithTheDefaultMessage()
    {
        foreach (var entry in ThrowableExceptions.All)
        {
            var code = Enum.Parse<GSharpErrorCode>(entry.CodeName);
            var exception = GSharpErrorMapper.CreateException(code, 0u, 0u, 0u, (0, 0, 0));
            Assert.Equal(entry.FullName, exception.GetType().FullName);
            Assert.Equal(entry.DefaultMessage, GSharpErrorMapper.GetDefaultMessage(code));
            Assert.Contains(entry.DefaultMessage, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Table_ParameterNameFlag_MatchesTheTypesThatHaveAParamName()
    {
        foreach (var entry in ThrowableExceptions.All)
        {
            var code = Enum.Parse<GSharpErrorCode>(entry.CodeName);
            var exception = GSharpErrorMapper.CreateException(
                code, 0u, 0u, 0u, (0, 0, 0), explicitMessage: "m (Parameter 'p')");
            var paramName = (exception as ArgumentException)?.ParamName;
            Assert.Equal(entry.HasParameterName ? "p" : null, paramName);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Throw_UnknownTypeInTheSite_FailsLoudlyInsteadOfAnotherType()
    {
        var site = new GSharpErrorSite(1u, "F.cs", 3, 5, "K", "text", "No.Such.Type, NoSuchAssembly");
        var module = GSharpErrorMapper.RegisterErrorSites("ThrowableExceptionTableTests", [site]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => GSharpErrorMapper.CreateException(GSharpErrorCode.Throw, 1u, module, 0u, (0, 0, 0)));

        Assert.Contains("G# internal error", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No.Such.Type", exception.Message, StringComparison.Ordinal);
    }
}
