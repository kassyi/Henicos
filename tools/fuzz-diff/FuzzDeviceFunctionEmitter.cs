using System.Globalization;
using System.Text;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Emits a topological DAG of up to 4 <c>[GpuDeviceFunction]</c> helper methods per kernel class
/// (with value, <see langword="in"/>, <see langword="ref"/>, and <see langword="out"/> parameters,
/// 1–2 generic type parameters, and an <see langword="in"/>-parameter defensive-copy probe)
/// and emits calls from kernels into the DAG including reordered named arguments.
/// </summary>
public static class FuzzDeviceFunctionEmitter
{
    /// <summary>
    /// Builds the topological DAG of 4 <c>[GpuDeviceFunction]</c> helper methods (<c>F_0</c> .. <c>F_3</c>)
    /// where each <c>F_i</c> only calls <c>F_j</c> with <c>j &lt; i</c>.
    /// </summary>
    public static IReadOnlyList<DeviceFunctionSpec> BuildDeviceFunctionDag(ulong fuzzSeed, string kernelClassName)
    {
        var rng = new FuzzRandom(FuzzRandom.Mix(fuzzSeed ^ 0x7F4A7C150002UL));
        int c0 = rng.NextRange(3, 29);
        int c1 = rng.NextRange(5, 43);
        int c2 = rng.NextRange(7, 61);
        int c3 = rng.NextRange(11, 79);

        var intType = FuzzTypeInfo.Get(FuzzScalarKind.Int32);
        var longType = FuzzTypeInfo.Get(FuzzScalarKind.Int64);

        var f0 = new DeviceFunctionSpec(
            "DevFunc_0",
            DagStage: 0,
            ReturnType: intType,
            Parameters:
            [
                new DeviceFunctionParam("snapVal", intType, "in"),
                new DeviceFunctionParam("target", intType, "ref"),
                new DeviceFunctionParam("delta", intType, string.Empty),
            ],
            TypeParameterNames: [],
            MethodSource: RenderDevFunc0(c0),
            IsInDefensiveCopyProbe: true);

        var f1 = new DeviceFunctionSpec(
            "DevFunc_1",
            DagStage: 1,
            ReturnType: intType,
            Parameters:
            [
                new DeviceFunctionParam("x", intType, string.Empty),
                new DeviceFunctionParam("y", longType, "in"),
                new DeviceFunctionParam("acc", intType, "ref"),
                new DeviceFunctionParam("sideOut", intType, "out"),
            ],
            TypeParameterNames: [],
            MethodSource: RenderDevFunc1(c1));

        var f2 = new DeviceFunctionSpec(
            "DevFunc_2",
            DagStage: 2,
            ReturnType: longType,
            Parameters:
            [
                new DeviceFunctionParam("a", intType, string.Empty),
                new DeviceFunctionParam("b", intType, string.Empty),
                new DeviceFunctionParam("stateVal", intType, "ref"),
            ],
            TypeParameterNames: ["T0"],
            MethodSource: RenderDevFunc2(c2));

        var f3 = new DeviceFunctionSpec(
            "DevFunc_3",
            DagStage: 3,
            ReturnType: intType,
            Parameters:
            [
                new DeviceFunctionParam("left", intType, string.Empty),
                new DeviceFunctionParam("right", intType, string.Empty),
                new DeviceFunctionParam("mirrorOut", intType, "out"),
            ],
            TypeParameterNames: ["T0", "T1"],
            MethodSource: RenderDevFunc3(kernelClassName, c3));

        return [f0, f1, f2, f3];
    }

    /// <summary>
    /// Emits mandatory <c>[GpuDeviceFunction]</c> DAG invocations in every kernel:
    /// (1) <see langword="in"/>-parameter defensive copy call <c>DevFunc_0(sL1.CompA, ref sL1, delta)</c>,
    /// (2) reordered named arguments with interfering side effects on <c>DevFunc_1</c>,
    /// and (3) generic <c>[GpuDeviceFunction]</c> calls <c>ClassName.DevFunc_2&lt;T0&gt;</c> and <c>ClassName.DevFunc_3&lt;T0, T1&gt;</c>.
    /// </summary>
    public static void EmitCoreDeviceFunctionCalls(KernelGenerationState state)
    {
        var rng = state.Rng;
        var i32 = state.FindLocal(FuzzScalarKind.Int32);
        var i64 = state.FindLocal(FuzzScalarKind.Int64);
        var (argA, argB) = state.PickDistinctPair(FuzzScalarKind.Int32, FuzzScalarKind.Int32);
        int outId = ++state.TempCounter;
        string outVar1 = string.Create(CultureInfo.InvariantCulture, $"devOut{outId}_a");
        string outVar2 = string.Create(CultureInfo.InvariantCulture, $"devOut{outId}_b");

        state.Statements.Add($"int {outVar1} = 0;");
        state.Statements.Add($"int {outVar2} = 0;");

        int delta = rng.NextRange(1, 19);
        state.Statements.Add(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{i32.Name} = ({i32.Name} ^ DevFunc_0(sL1.CompA, ref sL1, ({argA.Name} | {delta})));"));

        int step = rng.NextRange(1, 11);
        state.Statements.Add(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{i32.Name} = ({i32.Name} + DevFunc_1(y: ({i64.Name} += {step}L), x: ((int){i64.Name} ^ {argA.Name}), sideOut: out {outVar1}, acc: ref {argB.Name}));"));

        string[] typeArgs = ["int", "long", "uint", "short", "byte", "FuzzStructL1"];
        string t0 = typeArgs[rng.NextBelow(typeArgs.Length)];
        string t1 = typeArgs[rng.NextBelow(typeArgs.Length)];
        state.Statements.Add(
            $"{i64.Name} = ({i64.Name} ^ {state.KernelClassName}.DevFunc_2<{t0}>({i32.Name}, {outVar1}, ref {argA.Name}));");
        state.Statements.Add(
            $"{i32.Name} = ({i32.Name} ^ {state.KernelClassName}.DevFunc_3<{t0}, {t1}>(right: ({argA.Name} += 3), left: {argA.Name}, mirrorOut: out {outVar2}));");
        state.Statements.Add($"{i32.Name} = ({i32.Name} + ({outVar1} ^ {outVar2}));");
    }

    /// <summary>Emits an additional random <c>[GpuDeviceFunction]</c> call selected by Swarm weights.</summary>
    public static void EmitRandomDeviceFunctionCall(KernelGenerationState state)
    {
        var rng = state.Rng;
        var i32 = state.FindLocal(FuzzScalarKind.Int32);
        var i64 = state.FindLocal(FuzzScalarKind.Int64);
        int which = rng.NextBelow(3);

        if (which == 0)
        {
            string deltaExpr = FuzzScalarExpressionEmitter.GenerateLeafExpression(state, i32.Type);
            state.Statements.Add($"{i32.Name} = ({i32.Name} + DevFunc_0(sL1.CompA, ref sL1, {deltaExpr}));");
        }
        else if (which == 1)
        {
            string aExpr = FuzzScalarExpressionEmitter.GenerateLeafExpression(state, i32.Type);
            string bExpr = FuzzScalarExpressionEmitter.GenerateLeafExpression(state, i32.Type);
            state.Statements.Add($"{i64.Name} = ({i64.Name} + {state.KernelClassName}.DevFunc_2<int>({aExpr}, {bExpr}, ref {i32.Name}));");
        }
        else
        {
            int tmpId = ++state.TempCounter;
            string outName = string.Create(CultureInfo.InvariantCulture, $"devTmp{tmpId}");
            state.Statements.Add($"int {outName} = 0;");
            state.Statements.Add(
                $"{i32.Name} = ({i32.Name} ^ {state.KernelClassName}.DevFunc_3<long, short>({i32.Name}, inB[gid], out {outName}) ^ {outName});");
        }
    }

    /// <summary>Appends all <c>[GpuDeviceFunction]</c> helper method declarations to <paramref name="sb"/>.</summary>
    public static void AppendDeviceFunctionDeclarations(StringBuilder sb, IReadOnlyList<DeviceFunctionSpec> deviceFunctions)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(deviceFunctions);
        using var writer = new SourceWriter();
        AppendDeviceFunctionDeclarations(writer, deviceFunctions);
        sb.Append(writer.ToString());
    }

    /// <summary>Appends all <c>[GpuDeviceFunction]</c> helper method declarations to <paramref name="writer"/>.</summary>
    public static void AppendDeviceFunctionDeclarations(SourceWriter writer, IReadOnlyList<DeviceFunctionSpec> deviceFunctions)
    {
        ArgumentNullException.ThrowIfNull(deviceFunctions);

        for (int i = 0; i < deviceFunctions.Count; i++)
        {
            if (i > 0)
            {
                writer.AppendLine();
            }

            writer.AppendLine(deviceFunctions[i].MethodSource);
        }
    }

    private static string RenderDevFunc0(int c0)
    {
        using var writer = new SourceWriter();
        writer.AppendLine("[GpuDeviceFunction]");
        using (writer.Scope("public static int DevFunc_0(in int snapVal, ref FuzzStructL1 target, int delta)"))
        {
            writer.AppendLine("int before = snapVal;");
            writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"target.A = (target.A + delta + {c0});"));
            writer.AppendLine("return (before ^ snapVal) + target.A;");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderDevFunc1(int c1)
    {
        using var writer = new SourceWriter();
        writer.AppendLine("[GpuDeviceFunction]");
        using (writer.Scope("public static int DevFunc_1(int x, in long y, ref int acc, out int sideOut)"))
        {
            writer.AppendLine("FuzzStructL1 temp = new FuzzStructL1 { A = x, B = (short)(y & 0x7FFF), Tag = 3 };");
            writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"int step = DevFunc_0(temp.CompA, ref temp, (x ^ {c1}));"));
            writer.AppendLine("sideOut = (step ^ (int)y);");
            writer.AppendLine("acc = (acc + sideOut);");
            writer.AppendLine("return (acc ^ temp.A);");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderDevFunc2(int c2)
    {
        using var writer = new SourceWriter();
        writer.AppendLine("[GpuDeviceFunction]");
        using (writer.Scope("public static long DevFunc_2<T0>(int a, int b, ref int stateVal) where T0 : unmanaged"))
        {
            writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"long wide = ((long)a << 16) ^ b ^ {c2}L;"));
            writer.AppendLine("int localOut = 0;");
            writer.AppendLine("int mid = DevFunc_1(a, in wide, ref stateVal, out localOut);");
            writer.AppendLine("return (wide + mid + localOut);");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderDevFunc3(string kernelClassName, int c3)
    {
        using var writer = new SourceWriter();
        writer.AppendLine("[GpuDeviceFunction]");
        using (writer.Scope("public static int DevFunc_3<T0, T1>(int left, int right, out int mirrorOut) where T0 : unmanaged where T1 : unmanaged"))
        {
            writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"int scratch = (left ^ {c3});"));
            writer.AppendLine($"long combined = {kernelClassName}.DevFunc_2<T0>(left, right, ref scratch);");
            writer.AppendLine("FuzzStructL1 box = new FuzzStructL1 { A = scratch, B = (short)right, Tag = 1 };");
            writer.AppendLine("mirrorOut = DevFunc_0(box.CompA, ref box, right);");
            writer.AppendLine("return ((int)combined ^ mirrorOut ^ box.A);");
        }

        return writer.ToString().TrimEnd();
    }
}
