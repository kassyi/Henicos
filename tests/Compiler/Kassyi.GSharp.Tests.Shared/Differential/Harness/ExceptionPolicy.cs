#nullable enable
using System;
using System.Globalization;
using System.Reflection;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Controls how exceptions thrown by <see cref="DifferentialCase.Oracle"/> are handled by <see cref="DifferentialRunner"/>.
/// </summary>
public enum OracleExceptionMode
{
    /// <summary>Default: an unanticipated exception in <see cref="DifferentialCase.Oracle"/> fails the test unless <see cref="DifferentialCase.ExpectedException"/> is set.</summary>
    None = 0,

    /// <summary>Alias for <see cref="None"/>: only explicit <see cref="DifferentialCase.ExpectedException"/> policies are allowed.</summary>
    ExplicitOnly = 0,

    /// <summary>
    /// Fuzzlyn-style automatic matching: if <see cref="SequentialOracle"/> throws the same exception type across all
    /// thread orders, that exception type becomes the expected <see cref="ExceptionPolicy"/> on
    /// <see cref="DifferentialCase.ExceptionPaths"/> for that seed.
    /// </summary>
    MatchOracle = 1,
}

/// <summary>
/// Controls subprocess sampling granularity when executing CUDA <c>trap;</c> fault seeds via <see cref="CudaTrapSubprocessRunner"/>.
/// </summary>
public enum CudaTrapSampling
{
    /// <summary>Executes at most the first fault seed per case in an isolated child process on CUDA (fast CI default).</summary>
    FirstFaultPerCase = 0,

    /// <summary>Executes every fault seed and launch shape in an isolated child process on CUDA (enabled when <c>GSHARP_DIFF_FULL_CUDA_TRAPS=1</c>).</summary>
    AllFaults = 1,
}

/// <summary>
/// Specifies the expected exception contract for a seed and launch shape in a differential test case.
/// </summary>
public sealed class ExceptionPolicy
{
    /// <summary>Initializes a new instance of the <see cref="ExceptionPolicy"/> class.</summary>
    /// <param name="exceptionType">The expected exception type.</param>
    /// <param name="paths">The execution paths required to throw the exception.</param>
    public ExceptionPolicy(Type exceptionType, DiffPath paths = DiffPath.Managed | DiffPath.Native)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        if (!typeof(Exception).IsAssignableFrom(exceptionType))
        {
            throw new ArgumentException($"Type '{exceptionType.FullName}' is not an Exception type.", nameof(exceptionType));
        }

        ExceptionType = exceptionType;
        Paths = paths;
    }

    /// <summary>Gets the expected exception type.</summary>
    public Type ExceptionType { get; }

    /// <summary>Gets or initializes the paths required to throw <see cref="ExceptionType"/>.</summary>
    public DiffPath Paths { get; init; }

    /// <summary>Gets or initializes the optional expected source file name.</summary>
    public string? SourceFile { get; init; }

    /// <summary>Gets or initializes the optional expected 1-based source line number.</summary>
    public int? Line { get; init; }

    /// <summary>Gets or initializes the optional expected 1-based source column number.</summary>
    public int? Column { get; init; }

    /// <summary>Gets or initializes the optional enclosing kernel or device method name matched against <c>ex.Data["GSharp.MemberName"]</c>.</summary>
    public string? MemberName { get; init; }

    /// <summary>Gets or initializes an optional substring that must appear in <see cref="Exception.Message"/>.</summary>
    public string? MessageContains { get; init; }

    /// <summary>Gets or initializes an optional expected <see cref="ArgumentException.ParamName"/>.</summary>
    public string? ParamName { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether on <see cref="DiffPath.Cuda"/> the thrown exception must wrap a
    /// <c>CudaException</c> as <see cref="Exception.InnerException"/>.
    /// </summary>
    public bool RequireCudaInnerException { get; init; }

    /// <summary>Creates an <see cref="ExceptionPolicy"/> for <typeparamref name="TException"/>.</summary>
    public static ExceptionPolicy For<TException>(
        DiffPath paths = DiffPath.Managed | DiffPath.Native,
        string? memberName = null,
        string? messageContains = null,
        string? paramName = null,
        bool requireCudaInnerException = false
    )
        where TException : Exception =>
        new(typeof(TException), paths)
        {
            MemberName = memberName,
            MessageContains = messageContains,
            ParamName = paramName,
            RequireCudaInnerException = requireCudaInnerException,
        };

    /// <summary>Alias for <see cref="For{TException}"/>.</summary>
    public static ExceptionPolicy Expect<TException>(
        DiffPath paths = DiffPath.Managed | DiffPath.Native,
        string? memberName = null,
        string? messageContains = null,
        string? paramName = null,
        bool requireCudaInnerException = false
    )
        where TException : Exception =>
        For<TException>(paths, memberName, messageContains, paramName, requireCudaInnerException);

    /// <summary>Alias for <see cref="For{TException}"/>.</summary>
    public static ExceptionPolicy ExpectException<TException>(
        DiffPath paths = DiffPath.Managed | DiffPath.Native,
        string? memberName = null,
        string? messageContains = null,
        string? paramName = null,
        bool requireCudaInnerException = false
    )
        where TException : Exception =>
        For<TException>(paths, memberName, messageContains, paramName, requireCudaInnerException);

    /// <summary>Unwraps <see cref="TargetInvocationException"/> to return the underlying thrown exception.</summary>
    public static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (current is TargetInvocationException { InnerException: { } inner })
        {
            current = inner;
        }

        return current;
    }

    /// <summary>Evaluates whether <paramref name="thrown"/> satisfies this policy on <paramref name="path"/>.</summary>
    public bool Matches(Exception thrown, DiffPath path, out string? mismatchReason)
    {
        var actual = Unwrap(thrown);
        if (actual.GetType() != ExceptionType)
        {
            mismatchReason = $"expected {ExceptionType.FullName}, got {actual.GetType().FullName}: {actual.Message}";
            return false;
        }

        if (!string.IsNullOrEmpty(MessageContains) && !actual.Message.Contains(MessageContains, StringComparison.Ordinal))
        {
            mismatchReason = $"expected message containing \"{MessageContains}\", got \"{actual.Message}\"";
            return false;
        }

        if (!string.IsNullOrEmpty(ParamName))
        {
            string? actualParam = (actual as ArgumentException)?.ParamName;
            if (actualParam != ParamName)
            {
                mismatchReason = $"expected ParamName \"{ParamName}\", got {(actualParam is null ? "none" : $"\"{actualParam}\"")}";
                return false;
            }
        }

        if (!string.IsNullOrEmpty(MemberName))
        {
            if (actual.Data["GSharp.MemberName"] is not string actualMember || string.IsNullOrEmpty(actualMember))
            {
                mismatchReason = $"expected MemberName \"{MemberName}\", got none";
                return false;
            }

            if (!actualMember.Contains(MemberName, StringComparison.Ordinal))
            {
                mismatchReason = $"expected MemberName containing \"{MemberName}\", got \"{actualMember}\"";
                return false;
            }
        }

        if (!string.IsNullOrEmpty(SourceFile) || Line is not null || Column is not null)
        {
            if (actual.Data["GSharp.SourceLocation"] is not string actualLocation || string.IsNullOrEmpty(actualLocation))
            {
                string expectedDesc = !string.IsNullOrEmpty(SourceFile)
                    ? $"SourceFile \"{SourceFile}\""
                    : Line is { } missingLine
                        ? $"Line {missingLine.ToString(CultureInfo.InvariantCulture)}"
                        : Column is { } missingCol
                            ? $"Column {missingCol.ToString(CultureInfo.InvariantCulture)}"
                            : "SourceLocation";
                mismatchReason = $"expected {expectedDesc} in SourceLocation, got none";
                return false;
            }

            if (!string.IsNullOrEmpty(SourceFile) && !actualLocation.Contains(SourceFile, StringComparison.OrdinalIgnoreCase))
            {
                mismatchReason = $"expected SourceLocation containing \"{SourceFile}\", got \"{actualLocation}\"";
                return false;
            }

            if (Line is { } expectedLine && !actualLocation.Contains($"({expectedLine.ToString(CultureInfo.InvariantCulture)}", StringComparison.Ordinal))
            {
                mismatchReason = $"expected Line {expectedLine} in SourceLocation, got \"{actualLocation}\"";
                return false;
            }

            if (Column is { } expectedCol && !actualLocation.Contains($",{expectedCol.ToString(CultureInfo.InvariantCulture)})", StringComparison.Ordinal))
            {
                mismatchReason = $"expected Column {expectedCol} in SourceLocation, got \"{actualLocation}\"";
                return false;
            }
        }

        if (path == DiffPath.Cuda && RequireCudaInnerException)
        {
            string? innerName = actual.InnerException switch
            {
                CudaTrapSubprocessRunner.SubprocessCudaInnerException sub => sub.OriginalTypeName,
                { } inner => inner.GetType().FullName ?? inner.GetType().Name,
                null => null,
            };
            if (innerName is null || !innerName.Contains("CudaException", StringComparison.Ordinal))
            {
                mismatchReason = $"expected CudaException InnerException on Cuda path, got {innerName ?? "null"}";
                return false;
            }
        }

        mismatchReason = null;
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => ExceptionType.Name;
}
