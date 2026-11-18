namespace Kassyi.GSharp.Compiler.Ir.Frames;

/// <summary>
/// A gen/kill transfer function over tracked variable bitsets, with sequential, conditional, and loop combinators.
/// </summary>
/// <param name="Gen">Variables read before being killed.</param>
/// <param name="Kill">Variables definitely written.</param>
internal readonly record struct IrTransfer(ulong[] Gen, ulong[] Kill)
{
    /// <summary>The number of 64-bit words required for <paramref name="variableCount"/> variables.</summary>
    public static int WordCount(int variableCount) => (variableCount + 63) / 64;

    /// <summary>An empty transfer function (neither reads nor writes).</summary>
    public static IrTransfer Empty(int variableCount)
    {
        var words = WordCount(variableCount);
        return new IrTransfer(new ulong[words], new ulong[words]);
    }

    /// <summary>A transfer function that reads variable <paramref name="index"/>.</summary>
    public static IrTransfer Read(int index, int variableCount)
    {
        var words = WordCount(variableCount);
        var gen = new ulong[words];
        gen[index / 64] = 1UL << (index % 64);
        return new IrTransfer(gen, new ulong[words]);
    }

    /// <summary>A transfer function that writes (kills) variable <paramref name="index"/>.</summary>
    public static IrTransfer Write(int index, int variableCount)
    {
        var words = WordCount(variableCount);
        var kill = new ulong[words];
        kill[index / 64] = 1UL << (index % 64);
        return new IrTransfer(new ulong[words], kill);
    }

    /// <summary>Sequential composition: evaluates <paramref name="first"/> then <paramref name="second"/>.</summary>
    public static IrTransfer Then(in IrTransfer first, in IrTransfer second)
    {
        var words = first.Gen.Length;
        var gen = new ulong[words];
        var kill = new ulong[words];
        for (var w = 0; w < words; w++)
        {
            gen[w] = first.Gen[w] | (second.Gen[w] & ~first.Kill[w]);
            kill[w] = first.Kill[w] | second.Kill[w];
        }

        return new IrTransfer(gen, kill);
    }

    /// <summary>
    /// Conditional composition: evaluates <paramref name="condition"/>, then either <paramref name="whenTrue"/> or
    /// <paramref name="whenFalse"/>.
    /// </summary>
    public static IrTransfer Either(in IrTransfer condition, in IrTransfer whenTrue, in IrTransfer whenFalse)
    {
        var words = condition.Gen.Length;
        var gen = new ulong[words];
        var kill = new ulong[words];
        for (var w = 0; w < words; w++)
        {
            gen[w] = condition.Gen[w] | ((whenTrue.Gen[w] | whenFalse.Gen[w]) & ~condition.Kill[w]);
            kill[w] = condition.Kill[w] | (whenTrue.Kill[w] & whenFalse.Kill[w]);
        }

        return new IrTransfer(gen, kill);
    }

    /// <summary>
    /// Loop composition: evaluates <paramref name="condition"/> before every iteration, and <paramref name="body"/>
    /// zero or more times.
    /// </summary>
    public static IrTransfer Loop(in IrTransfer condition, in IrTransfer body)
    {
        var words = condition.Gen.Length;
        var gen = new ulong[words];
        var kill = new ulong[words];
        for (var w = 0; w < words; w++)
        {
            gen[w] = condition.Gen[w] | (body.Gen[w] & ~condition.Kill[w]);
            kill[w] = condition.Kill[w];
        }

        return new IrTransfer(gen, kill);
    }

    /// <summary>Computes backward <c>in = gen ∪ (out − kill)</c> into <paramref name="liveIn"/>.</summary>
    public static void Apply(in IrTransfer transfer, ulong[] liveOut, ulong[] liveIn)
    {
        for (var w = 0; w < liveIn.Length; w++)
        {
            liveIn[w] = transfer.Gen[w] | (liveOut[w] & ~transfer.Kill[w]);
        }
    }

    /// <summary>Whether variable <paramref name="index"/> is in <see cref="Gen"/>.</summary>
    public bool HasGen(int index) => (Gen[index / 64] & (1UL << (index % 64))) != 0;

    /// <summary>Whether variable <paramref name="index"/> is in <see cref="Kill"/>.</summary>
    public bool HasKill(int index) => (Kill[index / 64] & (1UL << (index % 64))) != 0;
}
