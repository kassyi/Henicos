#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

using static FermatTestMath;

/// <summary>
/// Tests that the signed form <c>|x| = min(x, N - x)</c> of a Pietrzak proof leaves no second encoding of a proof
/// through the element <c>-1</c> of order 2.
/// </summary>
/// <remarks>
/// <para>
/// Properties checked:
/// <list type="bullet">
/// <item><description><b>P-negation-rejected</b>: a proof that negates <c>u_K</c> and <c>mu_0</c> together (which keeps
/// every halving check true in the full group <c>Z_N^*</c>) is rejected.</description></item>
/// <item><description><b>P-negation-collapses</b>: the same forgery written in the signed form is the honest proof,
/// accepted with the reference verdict, so negation cannot change the verdict.</description></item>
/// </list>
/// </para>
/// </remarks>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class FermatPrpProofSignedFormTests
{
    // E = 5 (N = 2027, prime) and k = 1 is the reproduction of the issue; the other cases add deeper trees, where the
    // later witnesses follow the challenges of the forged values, and a composite N.
    public static TheoryData<int, int> ForgeryCases =>
        new()
        {
            { 5, 1 },
            { 29, 2 },
            { 31, 2 },
            { 31, 3 },
        };

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(ForgeryCases))]
    public void VerifyProof_EndpointAndFirstWitnessNegatedTogether_IsRejected(int e, int proofPower)
    {
        var honest = RunHonestProof(e, proofPower);

        var forged = ForgeNegatedEndpoint(honest, signedForm: false);

        // mutant: PietrzakProofVerifier.cs IsNonZeroSignedResidue `&& math.IsSigned(words)` removed (k = 1 is accepted)
        var verification = FermatAlgorithm.VerifyProof(forged);

        Assert.False(verification.IsValid, $"Forged proof accepted at E = {e}, k = {proofPower}.");
        Assert.False(verification.IsProbablePrime);
        Assert.Contains("signed form", verification.FailureReason);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(ForgeryCases))]
    public void VerifyProof_NegatedForgeryInSignedForm_IsTheHonestProofWithTheReferenceVerdict(int e, int proofPower)
    {
        var honest = RunHonestProof(e, proofPower);

        var forged = ForgeNegatedEndpoint(honest, signedForm: true);

        Assert.Equal(FromWords(honest.ResidueK.Span), FromWords(forged.ResidueK.Span));
        for (var t = 0; t < proofPower; t++)
        {
            Assert.Equal(FromWords(honest.Witnesses[t].Span), FromWords(forged.Witnesses[t].Span));
        }

        // mutant: PietrzakArithmetic.cs ComputeEndpointVerdict `Abs(...)` removed around ReadCanonical (wrong verdict)
        var verification = FermatAlgorithm.VerifyProof(forged);

        Assert.True(verification.IsValid, verification.FailureReason);
        Assert.Equal(PrpReference(e, 3).IsProbablePrime, verification.IsProbablePrime);
    }

    private static FermatPrpProof RunHonestProof(int e, int proofPower)
    {
        using var runner = new FermatAlgorithm(e, 3, new FermatPrpOptions { ProofPower = proofPower });
        runner.Run();
        var honest = runner.LastProof;
        Assert.NotNull(honest);
        var honestVerification = FermatAlgorithm.VerifyProof(honest);
        Assert.True(honestVerification.IsValid, honestVerification.FailureReason);
        Assert.Equal(PrpReference(e, 3).IsProbablePrime, honestVerification.IsProbablePrime);
        return honest;
    }

    /// <summary>
    /// Forges a proof of <c>-u_K</c>: negates <c>u_K</c> and the first witness <c>mu_0</c>, and computes the later
    /// witnesses honestly for the challenges the verifier derives from the forged values. In the full group
    /// <c>Z_N^*</c> the sign cancels (the challenges are odd and <c>X^(2^d)</c> is a square), so every halving check
    /// holds. With <paramref name="signedForm"/>, every value sent or hashed is written as <c>|x|</c>, as the verifier
    /// requires.
    /// </summary>
    private static FermatPrpProof ForgeNegatedEndpoint(FermatPrpProof honest, bool signedForm)
    {
        var header = honest.Header;
        var words = header.ResidueWords;
        var n = ModulusOf(header.E);
        var leafCount = 1 << header.ProofPower;
        var stepSize = header.E / leafCount;
        var remainder = header.E - leafCount * stepSize;
        BigInteger Encode(BigInteger value) => signedForm ? Signed(value, n) : value;

        var uE = FromWords(honest.ResidueE.Span);
        var forgedUk = Encode(n - FromWords(honest.ResidueK.Span));
        var schedule = new PietrzakSchedule(header);
        var alpha = (BigInteger)schedule.DeriveMergeChallenge(honest.ResidueE.Span, ToWords(forgedUk, words));

        var a0 = BigInteger.ModPow(header.Base, BigInteger.One << remainder, n);
        var b0 = BigInteger.ModPow(uE, BigInteger.One << (1 + remainder), n);
        var x = BigInteger.ModPow(b0, alpha, n) * a0 % n;
        var y = BigInteger.ModPow(forgedUk, alpha, n) * uE % n;
        var length = stepSize * leafCount;
        var witnesses = new ReadOnlyMemory<uint>[header.ProofPower];
        for (var round = 0; round < header.ProofPower; round++)
        {
            length /= 2;
            var mu = BigInteger.ModPow(x, BigInteger.One << length, n);
            if (round == 0)
            {
                mu = n - mu;
            }

            mu = Encode(mu);
            witnesses[round] = ToWords(mu, words);
            var claim = new PietrzakClaim(ToWords(Encode(x), words), ToWords(Encode(y), words));
            var challenge = (BigInteger)schedule.DeriveRoundChallenge(round, claim, witnesses[round].Span);
            x = BigInteger.ModPow(x, challenge, n) * mu % n;
            y = BigInteger.ModPow(mu, challenge, n) * y % n;
        }

        return new FermatPrpProof(header, honest.ResidueE, ToWords(forgedUk, words), witnesses);
    }
}
