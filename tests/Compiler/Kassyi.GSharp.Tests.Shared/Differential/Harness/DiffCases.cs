#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Factories for constructing <see cref="DifferentialCase"/> instances directly from generated <c>Launch*</c>
/// method groups without repeating the kernel name or boilerplate launch lambdas.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>oracleIsLaunch</c> defaults to <see langword="true"/> when both <c>oracle</c> and <c>reference</c> are omitted; <c>Verify</c>-only or <c>ExpectedException</c>-only cases without <c>Oracle</c>/<c>Reference</c> must pass <c>oracleIsLaunch: false</c>.</description></item>
///   <item><description>Generic <c>Create&lt;...&gt;</c> and <c>CreateRows&lt;...&gt;</c> overloads require explicit type arguments when passed a method group.</description></item>
/// </list>
/// </remarks>
internal static class DiffCases
{
    private static DifferentialCase Build(
        string launchExpression,
        string? suffix,
        string? caseName,
        string launchParamName,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        Action<DiffBuffers, LaunchShape> launch,
        LaunchShape? shape,
        IReadOnlyList<LaunchShape>? shapes,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only,
        Action<DiffBuffers, LaunchShape>? reference,
        Action<DiffBuffers, LaunchShape>? oracle,
        bool? oracleIsLaunch,
        IReadOnlyList<int>? seeds,
        OutputPolicy? policy,
        OracleExceptionMode oracleExceptions,
        ExceptionPolicy? exceptionPolicy,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException,
        DiffPath? exceptionPaths,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial)
    {
        if (shape is null && shapes is not { Count: > 0 })
        {
            throw new ArgumentException("Either shape or a non-empty shapes list must be provided.", nameof(shape));
        }

        if (shape is not null && shapes is not null)
        {
            throw new ArgumentException("Specify either shape or shapes, not both.", nameof(shapes));
        }

        if (caseName is not null && suffix is not null)
        {
            throw new ArgumentException("Specify either caseName or suffix, not both.", nameof(suffix));
        }

        string name = caseName ?? DiffKernelNames.FromLaunchExpression(launchExpression, suffix, launchParamName);
        bool effectiveOracleIsLaunch = oracleIsLaunch ?? (oracle is null && reference is null);
        DiffPath effectiveExceptionPaths = exceptionPaths ?? DiffPaths.CheckedPaths();

        if (shape is not null)
        {
            return new DifferentialCase(name)
            {
                Shape = shape,
                Shapes = shapes,
                ExtraShapesSeed0Only = extraShapesSeed0Only,
                Inputs = inputs,
                Launch = launch,
                Oracle = oracle,
                OracleIsLaunch = effectiveOracleIsLaunch,
                Reference = reference,
                Seeds = seeds,
                Policy = policy,
                OracleExceptions = oracleExceptions,
                ExceptionPolicy = exceptionPolicy,
                ExpectedException = expectedException,
                ExceptionPaths = effectiveExceptionPaths,
                SkippedPaths = skippedPaths,
                Verify = verify,
                VerifyAgainstInitial = verifyAgainstInitial,
            };
        }

        return new DifferentialCase(name)
        {
            Shapes = shapes,
            ExtraShapesSeed0Only = extraShapesSeed0Only,
            Inputs = inputs,
            Launch = launch,
            Oracle = oracle,
            OracleIsLaunch = effectiveOracleIsLaunch,
            Reference = reference,
            Seeds = seeds,
            Policy = policy,
            OracleExceptions = oracleExceptions,
            ExceptionPolicy = exceptionPolicy,
            ExpectedException = expectedException,
            ExceptionPaths = effectiveExceptionPaths,
            SkippedPaths = skippedPaths,
            Verify = verify,
            VerifyAgainstInitial = verifyAgainstInitial,
        };
    }

    public static DifferentialCase Create(
        Int32Launch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string bufferName = "a",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, bufferName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        Int32PairLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string inputName = "a",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, inputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        Int32TripletLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string firstInputName = "a",
        string secondInputName = "b",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, firstInputName, secondInputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        Int32RowsLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string inputName = "a",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, inputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        Int32PairRowsLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string firstInputName = "a",
        string secondInputName = "b",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, firstInputName, secondInputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        Int32ScalarLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string bufferName = "a",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, bufferName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        Int32ToInt64Launch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string inputName = "a",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, inputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        Int64RowsLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string inputName = "a",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, inputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        Int64PairRowsLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string firstInputName = "a",
        string secondInputName = "b",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, firstInputName, secondInputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        Int64ScalarLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string bufferName = "a",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, bufferName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    /// <summary>
    /// Creates a <see cref="DifferentialCase"/> from an <see cref="Int32NullablePairLaunch"/> kernel (<c>(int[], int[]?)</c>).
    /// Named distinctly from <see cref="Create(Int32PairLaunch, Func{DiffRandom, LaunchShape, DiffBuffers}, LaunchShape?, IReadOnlyList{LaunchShape}?, IReadOnlyList{LaunchShape}?, string, string, string?, string?, Action{DiffBuffers, LaunchShape}?, Action{DiffBuffers, LaunchShape}?, bool?, IReadOnlyList{int}?, OutputPolicy?, OracleExceptionMode, ExceptionPolicy?, Func{DiffRandom, LaunchShape, ExceptionPolicy?}?, DiffPath?, IReadOnlyDictionary{DiffPath, string}?, Func{DiffBuffers, LaunchShape, PropertyResult}?, Func{DiffBuffers, DiffBuffers, LaunchShape, PropertyResult}?, uint, string)"/>
    /// because C# erases nullability during method-group overload resolution.
    /// </summary>
    public static DifferentialCase CreateNullablePair(
        Int32NullablePairLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string firstName = "o",
        string nullableSecondName = "a",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.BindNullablePair(launch, firstName, nullableSecondName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        SinglePairLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string inputName = "a",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, inputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        SingleRowsLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string inputName = "a",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, inputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        UInt32TripletLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string firstInputName = "a",
        string secondInputName = "b",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, firstInputName, secondInputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        UInt32TripletRowsLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string firstInputName = "a",
        string secondInputName = "b",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, firstInputName, secondInputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        UInt64TripletRowsLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string firstInputName = "a",
        string secondInputName = "b",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, firstInputName, secondInputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    public static DifferentialCase Create(
        Int32QuadLaunch launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string firstName = "a",
        string secondName = "b",
        string thirdName = "c",
        string fourthName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, firstName, secondName, thirdName, fourthName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    /// <summary>
    /// Creates a <see cref="DifferentialCase"/> from a generic 1-buffer <c>Launch*</c> method group.
    /// Explicit type arguments are required for method groups; <c>Verify</c>-only/<c>ExpectedException</c>-only cases without
    /// <c>Oracle</c>/<c>Reference</c> must pass <c>oracleIsLaunch: false</c>.
    /// </summary>
    public static DifferentialCase Create<TBuffer>(
        DiffLaunch<TBuffer[]> launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string bufferName = "a",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, bufferName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    /// <summary>
    /// Creates a <see cref="DifferentialCase"/> from a generic 2-buffer <c>Launch*</c> method group.
    /// Explicit type arguments are required for method groups; <c>Verify</c>-only/<c>ExpectedException</c>-only cases without
    /// <c>Oracle</c>/<c>Reference</c> must pass <c>oracleIsLaunch: false</c>.
    /// </summary>
    public static DifferentialCase Create<TSource, TResult>(
        DiffLaunch<TSource[], TResult[]> launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string inputName = "a",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, inputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    /// <summary>
    /// Creates a <see cref="DifferentialCase"/> from a generic 3-buffer <c>Launch*</c> method group.
    /// Explicit type arguments are required for method groups; <c>Verify</c>-only/<c>ExpectedException</c>-only cases without
    /// <c>Oracle</c>/<c>Reference</c> must pass <c>oracleIsLaunch: false</c>.
    /// </summary>
    public static DifferentialCase Create<TFirst, TSecond, TResult>(
        DiffLaunch<TFirst[], TSecond[], TResult[]> launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string firstInputName = "a",
        string secondInputName = "b",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, firstInputName, secondInputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    /// <summary>
    /// Creates a <see cref="DifferentialCase"/> from a generic 4-buffer <c>Launch*</c> method group.
    /// Explicit type arguments are required for method groups; <c>Verify</c>-only/<c>ExpectedException</c>-only cases without
    /// <c>Oracle</c>/<c>Reference</c> must pass <c>oracleIsLaunch: false</c>.
    /// </summary>
    public static DifferentialCase Create<TFirst, TSecond, TThird, TResult>(
        DiffLaunch<TFirst[], TSecond[], TThird[], TResult[]> launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string firstInputName = "a",
        string secondInputName = "b",
        string thirdInputName = "c",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.Bind(launch, firstInputName, secondInputName, thirdInputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    /// <summary>
    /// Creates a <see cref="DifferentialCase"/> from a generic 2-buffer row-counted <c>Launch*</c> method group.
    /// Explicit type arguments are required for method groups; <c>Verify</c>-only/<c>ExpectedException</c>-only cases without
    /// <c>Oracle</c>/<c>Reference</c> must pass <c>oracleIsLaunch: false</c>.
    /// </summary>
    public static DifferentialCase CreateRows<TSource, TResult>(
        DiffLaunch<TSource[], TResult[], int> launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string inputName = "a",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.BindRows(launch, inputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);

    /// <summary>
    /// Creates a <see cref="DifferentialCase"/> from a generic 3-buffer row-counted <c>Launch*</c> method group.
    /// Explicit type arguments are required for method groups; <c>Verify</c>-only/<c>ExpectedException</c>-only cases without
    /// <c>Oracle</c>/<c>Reference</c> must pass <c>oracleIsLaunch: false</c>.
    /// </summary>
    public static DifferentialCase CreateRows<TFirst, TSecond, TResult>(
        DiffLaunch<TFirst[], TSecond[], TResult[], int> launch,
        Func<DiffRandom, LaunchShape, DiffBuffers> inputs,
        LaunchShape? shape = null,
        IReadOnlyList<LaunchShape>? shapes = null,
        IReadOnlyList<LaunchShape>? extraShapesSeed0Only = null,
        string firstInputName = "a",
        string secondInputName = "b",
        string outputName = "dst",
        string? suffix = null,
        string? caseName = null,
        Action<DiffBuffers, LaunchShape>? reference = null,
        Action<DiffBuffers, LaunchShape>? oracle = null,
        bool? oracleIsLaunch = null,
        IReadOnlyList<int>? seeds = null,
        OutputPolicy? policy = null,
        OracleExceptionMode oracleExceptions = OracleExceptionMode.None,
        ExceptionPolicy? exceptionPolicy = null,
        Func<DiffRandom, LaunchShape, ExceptionPolicy?>? expectedException = null,
        DiffPath? exceptionPaths = null,
        IReadOnlyDictionary<DiffPath, string>? skippedPaths = null,
        Func<DiffBuffers, LaunchShape, PropertyResult>? verify = null,
        Func<DiffBuffers, DiffBuffers, LaunchShape, PropertyResult>? verifyAgainstInitial = null,
        uint sharedMemBytes = 0,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Build(
            launchExpression, suffix, caseName, nameof(launch),
            inputs, DiffLaunches.BindRows(launch, firstInputName, secondInputName, outputName, sharedMemBytes),
            shape, shapes, extraShapesSeed0Only, reference, oracle, oracleIsLaunch, seeds, policy,
            oracleExceptions, exceptionPolicy, expectedException, exceptionPaths, skippedPaths,
            verify, verifyAgainstInitial);
}
