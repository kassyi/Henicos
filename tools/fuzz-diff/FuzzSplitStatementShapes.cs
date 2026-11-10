using System.Globalization;
using static Kassyi.GSharp.Tools.FuzzDiff.FuzzSplitKernelEmitter;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// One shape of split statement: whether it is avoided for #991 or needs the stack array, and how it is written.
/// </summary>
internal sealed record FuzzSplitShape(bool ArgumentOrderIssue, bool NeedsArray, Action<FuzzSplitKernelBuilder> Emit);

/// <summary>
/// The shapes of split statement per kind of split point. <c>A</c>, <c>B</c> and <c>C</c> are distinct int locals;
/// <c>B</c> is the one a statement both writes and reads around its split point.
/// </summary>
internal static class FuzzSplitStatementShapes
{
    /// <summary>Warp shuffles as operands, arguments, indices and compound-assignment values.</summary>
    internal static readonly FuzzSplitShape[] Shuffle =
    [
        new(false, false, static b => b.Add($"{b.A} = (({b.B} = {b.Expr()}) + {b.Shuffle(b.B)});")),
        new(false, false, static b => b.Add($"{b.A} = ({SideBump}(ref {b.B}, {b.K()}) + {b.Shuffle(b.B)});")),
        new(false, false, static b => b.Add($"{b.A} = ({b.Shuffle(b.B)} - ({b.B} += {b.K()}));")),
        new(false, false, static b => b.Add($"{b.A} = ({b.Expr()} {b.Op()} {b.Shuffle(b.Expr())});")),
        new(true, false, static b => b.Add(
            $"{b.A} = (({b.B} += {b.K()}) ^ ({b.Uniform()} ? {b.Shuffle(b.B)} : {b.C}));")),
        new(true, false, static b => b.Add(
            $"{b.A} = (({b.B} ^= {b.K()}) | (({b.Uniform()} || ({b.Shuffle(b.B)} < {b.C})) ? 1 : 0));")),
        new(true, false, static b => b.Add(
            $"{b.A} = {SideMix}({b.B}, {b.Shuffle($"({b.B} = {b.Expr()})")}, {b.B});")),
        new(false, false, static b => b.Add($"{b.A} += {b.Shuffle($"{b.B} ^ {b.K()}")};")),
        new(true, false, static b => b.Add($"{b.A} += {b.Shuffle(b.Expr())};")),
        new(true, false, static b => b.Add($"{b.A} += {b.Shuffle($"{SideBump}(ref {b.A}, {b.K()})")};")),
        new(false, false, static b => b.Add($"{b.A} = inB[(gid & ~3) + ({b.Shuffle(b.Expr())} & 3)];")),
        new(false, true, static b => b.Add($"arr[{b.Shuffle(b.Expr())} & 3] += {b.A};")),
        new(false, false, static b => b.Add(
            $"{b.UInt} = (({b.UInt} * 3u) + " +
            $"Warp.ShuffleXorSync({FuzzSplitOracle.FullMaskLiteral}, ({b.UInt} ^ {b.K()}u), 0));")),
        new(false, false, static b => b.Add(
            $"{b.Long} = ((long){b.Shuffle($"(int)({b.Long} >> 7)")} + {b.Long});")),
    ];

    /// <summary>Block barriers between statements, in counted loops and in branches every thread takes.</summary>
    internal static readonly FuzzSplitShape[] Barrier =
    [
        new(false, false, EmitBarrierThroughStruct),
        new(false, false, EmitBarrierLoop),
        new(false, false, EmitBarrierBranch),
        new(false, true, EmitBarrierThroughArray),
        new(false, false, EmitDoubleBarrier),
    ];

    /// <summary>A <c>foreach</c> over a device iterator between split points.</summary>
    internal static readonly FuzzSplitShape[] Iterator =
    [
        new(false, false, EmitIteratorA),
        new(false, false, EmitIteratorB),
    ];

    /// <summary>Calls of suspending device functions as arguments and operands, in the order shapes of #991.</summary>
    internal static readonly FuzzSplitShape[] SuspendingCall =
    [
        new(false, false, static b => b.Add($"{b.A} = ({b.B} + {SuspendRef}(ref {b.B}, {b.K()}));")),
        new(false, false, static b => b.Add($"{b.A} = {SuspendSync}({b.B}, ({b.B} = {b.Expr()}));")),
        new(false, false, static b => b.Add(
            $"{b.A} = {SuspendSync}(({b.B} += {b.K()}), {SideMix}({b.B}, {b.K()}, {b.C}));")),
        new(false, false, static b => b.Add(
            $"{b.A} = {SuspendSync}({b.C}, ({b.B} + {SideBump}(ref {b.B}, {b.K()})));")),
        new(false, false, static b => b.Add(
            $"{b.A} = {SuspendSync}({b.C}, ({b.B} + {SuspendRef}(ref {b.B}, {b.K()})));")),
        new(true, false, static b => b.Add(
            $"{b.A} = {SideMix}(({b.B} = {b.Expr()}), " +
            $"({b.Uniform()} ? {SuspendSync}({b.B}, {b.K()}) : {b.K()}), {b.B});")),
        new(false, false, EmitOutArgument),
        new(true, false, static b => b.Add($"{b.B} += {SuspendRef}(ref {b.B}, {b.K()});")),
        new(false, false, static b => b.Add($"{b.A} = ({SuspendShuffle}({b.B}) ^ {b.Expr()});")),
        new(false, false, static b => b.Add($"{b.A} = ({b.Expr()} - {SuspendSync}({b.B}, {b.C}));")),
        new(false, false, static b => b.Add(
            $"{b.A} = {SuspendSync}({SuspendShuffle}({b.B}), {SuspendRef}(ref {b.C}, {b.K()}));")),
        new(false, false, static b => b.Add($"{b.Long} += (long){SuspendSync}((int){b.Long}, {b.B});")),
    ];

    private static void EmitBarrierThroughStruct(FuzzSplitKernelBuilder b)
    {
        b.Add($"{b.A} = {b.Expr()};");
        b.Add($"sv.A = ({b.B} ^ {b.K()});");
        b.Add("Gpu.SyncThreads();");
        b.Add($"{b.C} = (sv.A + {b.A});");
    }

    private static void EmitBarrierLoop(FuzzSplitKernelBuilder b)
    {
        string k = b.NewName("k");
        int bound = b.Rng.NextRange(2, 3);
        using var writer = new SourceWriter();
        using (writer.Scope(string.Create(CultureInfo.InvariantCulture, $"for (int {k} = 0; {k} < {bound}; {k}++)")))
        {
            writer.AppendLine($"{b.A} = (({b.A} * 7) + {k} + {b.Expr()});");
            writer.AppendLine("Gpu.SyncThreads();");
            writer.AppendLine($"{b.B} ^= ({b.A} >> 3);");
        }

        b.Add(writer.ToString().AsSpan().TrimEnd().ToString());
    }

    private static void EmitBarrierBranch(FuzzSplitKernelBuilder b)
    {
        using var writer = new SourceWriter();
        using (writer.Scope($"if ({b.Uniform()})"))
        {
            writer.AppendLine($"{b.A} += {b.K()};");
            writer.AppendLine("Gpu.SyncThreads();");
            writer.AppendLine($"{b.B} = ({b.A} - {b.B});");
        }

        using (writer.Scope("else"))
        {
            writer.AppendLine($"{b.B} += {b.K()};");
            writer.AppendLine("Gpu.SyncThreads();");
            writer.AppendLine($"{b.A} ^= {b.B};");
        }

        b.Add(writer.ToString().AsSpan().TrimEnd().ToString());
    }

    private static void EmitBarrierThroughArray(FuzzSplitKernelBuilder b)
    {
        b.Add($"arr[{b.B} & 3] = {b.A};");
        b.Add("Gpu.SyncThreads();");
        b.Add($"{b.C} = (arr[{b.A} & 3] + arr[0]);");
    }

    private static void EmitDoubleBarrier(FuzzSplitKernelBuilder b)
    {
        b.Add($"{b.A} = (({b.B} * {b.K()}) ^ {b.Expr()});");
        b.Add("Gpu.SyncThreads();");
        b.Add("Gpu.SyncThreads();");
        b.Add($"{b.B} += {b.A};");
    }

    private static void EmitIteratorA(FuzzSplitKernelBuilder b)
    {
        string item = b.NewName("it");
        string count = b.Rng.NextRange(2, 4).ToString(CultureInfo.InvariantCulture);
        string bumped = b.Rng.Choose(b.Ints).Name;
        b.Add("Gpu.SyncThreads();");
        using var writer = new SourceWriter();
        using (writer.Scope($"foreach (int {item} in {SplitIterA}({b.A}, {count}))"))
        {
            writer.AppendLine($"{b.B} = (({b.B} * 31) + {item});");
            writer.AppendLine($"{b.C} ^= {SideBump}(ref {bumped}, {item});");
        }

        b.Add(writer.ToString().AsSpan().TrimEnd().ToString());
        b.Add($"{b.A} = {b.Shuffle(b.B)};");
    }

    private static void EmitIteratorB(FuzzSplitKernelBuilder b)
    {
        string item = b.NewName("it");
        string step = b.K();
        using var writer = new SourceWriter();
        using (writer.Scope($"foreach (int {item} in {SplitIterB}({b.A}, {step}))"))
        {
            using (writer.Scope($"if (({item} & 7) == 5)"))
            {
                writer.AppendLine("break;");
            }

            writer.AppendLine($"{b.B} += {SideMix}({item}, {b.C}, {step});");
        }

        b.Add(writer.ToString().AsSpan().TrimEnd().ToString());
        b.Add("Gpu.SyncThreads();");
        b.Add($"{b.C} = ({b.B} - {b.Shuffle(b.C)});");
    }

    private static void EmitOutArgument(FuzzSplitKernelBuilder b)
    {
        string outName = b.NewName("o");
        b.Add($"int {outName} = 0;");
        b.Add($"{b.A} = {SideMix}({SuspendOut}({b.B}, {b.C}, out {outName}), {outName}, {b.K()});");
        b.AddIntLocal(outName);
    }
}
