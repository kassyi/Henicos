using Kassyi.GSharp.Compiler.Backends.Cpp.Support;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Bodies;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Sequencing;

namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// Per-route lowering of an <see cref="IrBodyFront"/>: evaluates the scan under the profile's split policy, runs
/// <see cref="IrDecisions"/> and <see cref="IrSequencer"/>, verifies invariants, and checks <see cref="IrCppSupport"/>.
/// </summary>
internal static class IrBodyLowering
{
    /// <summary>
    /// The IR body of <paramref name="front"/> for the route of <paramref name="profile"/>, or why it has none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The decided or the sequenced body breaks an invariant.</exception>
    public static IrBodyResult TryLower(IrBodyFront front, CppBodyProfile profile,
        CancellationToken cancellationToken) =>
        TryLower(front, profile, IrDecisions.Decide, cancellationToken);

    internal static IrBodyResult TryLower(IrBodyFront front, CppBodyProfile profile,
        Func<IrMethod, IrDecisionContext, IrMethod> decide, CancellationToken cancellationToken)
    {
        if (front.Classified is not { } classified || front.Scan is not { } scan)
        {
            return IrBodyResult.Ineligible(profile.Route, IrBodyIneligibility.IrNotBuilt, "");
        }

        var (reason, detail) = scan.Reason(profile.Splits);
        if (reason != IrBodyIneligibility.None)
        {
            return IrBodyResult.Ineligible(profile.Route, reason, detail);
        }

        var context = new IrDecisionContext(front.Compilation, IrTemporaryInitializers.Create(classified.Body),
            profile.Dialect, classified);
        IrMethod decided;
        using (IrBodyProbe.Measure(IrBodyStage.Decide))
        {
            decided = decide(classified, context);
            ThrowOnViolations("decided", IrDecisionInvariants.Verify(decided.Body, context.TemporaryInitializers));
        }

        IrMethod sequenced;
        using (IrBodyProbe.Measure(IrBodyStage.Sequence))
        {
            sequenced = IrSequencer.Sequence(decided, new IrSequenceContext(context));
            ThrowOnViolations("sequenced", IrSequenceInvariants.Verify(sequenced.Body));
        }

        ImmutableArray<IrSupportKey> unsupported;
        using (IrBodyProbe.Measure(IrBodyStage.Support))
        {
            unsupported = IrCppSupport.Check(sequenced, profile);
        }

        return unsupported.IsEmpty
            ? IrBodyResult.Eligible(profile.Route, new IrBody(front, profile, decided, sequenced, context))
            : new IrBodyResult(profile.Route, null, IrBodyIneligibility.UnsupportedForm, unsupported[0].ToString(),
                unsupported);
    }

    private static void ThrowOnViolations(string tree, ImmutableArray<string> violations)
    {
        if (!violations.IsEmpty)
        {
            throw new InvalidOperationException(
                $"The {tree} IR body breaks its invariants: {string.Join("; ", violations)}");
        }
    }
}
