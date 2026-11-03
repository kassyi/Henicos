using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Emits composite expressions and statements: nested struct initialization and <c>with</c> expressions,
/// user-defined conversions, <see cref="Nullable{T}"/> (<c>??</c>, <c>??=</c>, <c>.HasValue</c>),
/// <see cref="Span{T}"/> / <see cref="ReadOnlySpan{T}"/> (<c>stackalloc</c>, masked indexing, <c>is null</c> / <c>is not null</c>),
/// <c>switch</c> expressions, <c>ref</c> ternary expressions, and tuple deconstruction statements.
/// </summary>
public static class FuzzCompositeExpressionEmitter
{
    /// <summary>
    /// Seeds composite struct locals, <see cref="Nullable{T}"/> slots, and fixed-size <c>stackalloc</c> <see cref="Span{T}"/> locals
    /// at the top of the kernel body.
    /// </summary>
    public static void InitializeCompositeLocals(KernelGenerationState state)
    {
        var rng = state.Rng;
        var i32 = state.FindLocal(FuzzScalarKind.Int32);
        var i16 = state.FindLocal(FuzzScalarKind.Int16);
        var u8 = state.FindLocal(FuzzScalarKind.Byte);
        var i64 = state.FindLocal(FuzzScalarKind.Int64);
        var u32 = state.FindLocal(FuzzScalarKind.UInt32);
        var u16 = state.FindLocal(FuzzScalarKind.UInt16);

        // 1. Nested structs up to depth 3 + user-defined conversion struct + Pack=1 / Pack=4 structs.
        state.Statements.Add($"FuzzWrapInt sWrap = {i32.Name};");
        state.StructLocals.Add(new StructLocalVariable("sWrap", "FuzzWrapInt", IsPacked1: false));

        state.Statements.Add($"FuzzStructL1 sL1 = new FuzzStructL1 {{ A = inA[gid], B = {i16.Name}, Tag = {u8.Name} }};");
        state.StructLocals.Add(new StructLocalVariable("sL1", "FuzzStructL1", IsPacked1: false));

        state.Statements.Add($"FuzzStructL2 sL2 = new FuzzStructL2 {{ Inner = sL1, C = {i64.Name}, D = {u32.Name} }};");
        state.StructLocals.Add(new StructLocalVariable("sL2", "FuzzStructL2", IsPacked1: false));

        state.Statements.Add($"FuzzStructL3 sL3 = new FuzzStructL3 {{ Mid = sL2, Wrap = sWrap, Flags = {u16.Name} }};");
        state.StructLocals.Add(new StructLocalVariable("sL3", "FuzzStructL3", IsPacked1: false));

        state.Statements.Add($"FuzzPackedStruct1 sPack1 = new FuzzPackedStruct1 {{ Head = {u8.Name}, MisalignedInt = inB[gid], Tail = {i16.Name} }};");
        state.StructLocals.Add(new StructLocalVariable("sPack1", "FuzzPackedStruct1", IsPacked1: true));

        state.Statements.Add($"FuzzPackedStruct4 sPack4 = new FuzzPackedStruct4 {{ Low = {i16.Name}, AlignedInt = inC[gid], Wide = {i64.Name} }};");
        state.StructLocals.Add(new StructLocalVariable("sPack4", "FuzzPackedStruct4", IsPacked1: false));

        // 2. Nullable<T> fields inside FuzzNullableBag (initialized without `: null` so #984 is not triggered on Native/Cuda).
        state.Statements.Add($"FuzzNullableBag sNullBag = new FuzzNullableBag {{ OptShort0 = (short?){i16.Name} }};");
        state.Statements.Add("if ((inA[gid] & 1) == 0) { sNullBag.OptInt0 = (int?)inA[gid]; }");
        state.Statements.Add("if ((inB[gid] & 1) != 0) { sNullBag.OptInt1 = (int?)inB[gid]; }");
        state.Statements.Add("if ((inC[gid] & 1) == 0) { sNullBag.OptLong0 = (long?)sL2.C; }");
        state.StructLocals.Add(new StructLocalVariable("sNullBag", "FuzzNullableBag", IsPacked1: false));

        state.NullableSlots.Add(new NullableTargetSlot("sNullBag.OptInt0", FuzzTypeInfo.Get(FuzzScalarKind.Int32), IsLocalVariable: false));
        state.NullableSlots.Add(new NullableTargetSlot("sNullBag.OptInt1", FuzzTypeInfo.Get(FuzzScalarKind.Int32), IsLocalVariable: false));
        state.NullableSlots.Add(new NullableTargetSlot("sNullBag.OptLong0", FuzzTypeInfo.Get(FuzzScalarKind.Int64), IsLocalVariable: false));

        // When #984 is not avoided (Profile M or --allow-issue 984), also declare Nullable<T> local variables!
        if (!state.IssueFilter.AvoidNullableLocalVariables)
        {
            state.Statements.Add($"int? optLocal0 = ((inA[gid] & 2) == 0 ? (int?){i32.Name} : null);");
            state.Statements.Add($"long? optLocal1 = ((inB[gid] & 2) != 0 ? (long?){i64.Name} : null);");
            state.NullableSlots.Add(new NullableTargetSlot("optLocal0", FuzzTypeInfo.Get(FuzzScalarKind.Int32), IsLocalVariable: true));
            state.NullableSlots.Add(new NullableTargetSlot("optLocal1", FuzzTypeInfo.Get(FuzzScalarKind.Int64), IsLocalVariable: true));
        }

        // 3. Fixed-size stackalloc Span<T> and ReadOnlySpan<T> (K in {4, 8}, masked indexing & (K - 1)).
        int spanLen = rng.NextBool() ? 4 : 8;
        int mask = spanLen - 1;
        state.Statements.Add(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Span<int> sp0 = stackalloc int[{spanLen}] {{ inA[gid], inB[gid], inC[gid], {i32.Name}{(spanLen == 8 ? $", sL1.A, (int){u32.Name}, sPack4.AlignedInt, (int)sWrap" : string.Empty)} }};"));
        state.Statements.Add($"sp0[inC[gid] & {mask}] = (sp0[inA[gid] & {mask}] ^ {i32.Name});");
        state.Statements.Add($"ReadOnlySpan<int> roSp0 = sp0;");
        state.SpanLocals.Add(new SpanLocalVariable("sp0", FuzzTypeInfo.Get(FuzzScalarKind.Int32), spanLen, IsReadOnly: false));
        state.SpanLocals.Add(new SpanLocalVariable("roSp0", FuzzTypeInfo.Get(FuzzScalarKind.Int32), spanLen, IsReadOnly: true));
    }

    /// <summary>Emits mandatory composite feature statements in every kernel so all composite constructs are exercised.</summary>
    public static void EmitCoreCompositeStatements(KernelGenerationState state)
    {
        var i32 = state.FindLocal(FuzzScalarKind.Int32);
        var i64 = state.FindLocal(FuzzScalarKind.Int64);
        var (tupleA, tupleB) = state.PickDistinctPair(FuzzScalarKind.Int32, FuzzScalarKind.Int32);

        // 1. Struct `with` expression + nested struct member mutation + readonly method/property + implicit/explicit conversion.
        if (!state.IssueFilter.AvoidPlainStructInstanceMethods)
        {
            state.Statements.Add($"sL1 = sL1 with {{ A = (sL1.Combine({i32.Name}) ^ sL3.Wrap.MixWith(inC[gid])) }};");
            state.Statements.Add($"sL3.Mid.Inner.A = (sL1.CompA + (int)sWrap);");
            state.Statements.Add($"sWrap = (sL3.Mid.Project({i32.Name}) + 17);");
        }
        else
        {
            state.Statements.Add($"sL1 = sL1 with {{ A = (sL1.CompA ^ sL3.Wrap.Shifted ^ inC[gid]) }};");
            state.Statements.Add($"sL3.Mid.Inner.A = (sL1.CompA + (int)sWrap);");
            state.Statements.Add($"sWrap = ((int)sL3.Mid.Summary + 17);");
        }

        state.Statements.Add($"{i64.Name} = ({i64.Name} ^ (long)sWrap ^ sL3.Total);");

        if (!state.IssueFilter.AvoidStructConversionDefault)
        {
            state.Statements.Add("FuzzWrapInt sWrapDef = default(FuzzWrapInt);");
            state.Statements.Add($"{i32.Name} = ({i32.Name} ^ (int)sWrapDef);");
        }

        // 2. Pack = 1 and Pack = 4 value and compound assignment.
        if (!state.IssueFilter.AvoidPlainStructInstanceMethods)
        {
            state.Statements.Add($"sPack1.MisalignedInt = (sPack1.ReadSum() ^ {i32.Name});");
        }
        else
        {
            state.Statements.Add($"sPack1.MisalignedInt = (sPack1.PackedSum ^ {i32.Name});");
        }

        state.Statements.Add($"sPack1.MisalignedInt += {i32.Name};");

        // 3. Nullable<T>: `??`, `??=` statement form, `??=` value-consuming form, and `.HasValue`.
        EmitNullableStatement(state);

        // 4. Span<T> / ReadOnlySpan<T>: masked indexing, `.Length`, `is null`, and `is not null`.
        EmitSpanStatement(state);

        // 5. Interfering tuple deconstruction: `(a, b) = ((b += delta), (a ^ b));` (catches RHS evaluation order reversal).
        int delta = state.Rng.NextRange(1, 13);
        if (tupleA.Name != tupleB.Name)
        {
            state.Statements.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"({tupleA.Name}, {tupleB.Name}) = (({tupleB.Name} += {delta}), ({tupleA.Name} ^ {tupleB.Name}));"));
            if (!state.IssueFilter.AvoidComplexTupleDeconstruction)
            {
                EmitTupleDeconstructionStatement(state);
            }
        }

        // 6. Ref ternary statement (`(cond ? ref a : ref b) += expr;`).
        EmitRefTernaryStatement(state);

        // 7. Switch expression assignment.
        string swExpr = GenerateSwitchExpression(state, i32.Type, depth: 1, maxDepth: 2);
        state.Statements.Add($"{i32.Name} = ({i32.Name} ^ {swExpr});");
    }

    /// <summary>Emits a <see cref="Nullable{T}"/> statement respecting <c>#998</c> and <c>#984</c> avoidance switches.</summary>
    public static void EmitNullableStatement(KernelGenerationState state)
    {
        var rng = state.Rng;
        var i32 = state.FindLocal(FuzzScalarKind.Int32);
        var i64 = state.FindLocal(FuzzScalarKind.Int64);
        var i16 = state.FindLocal(FuzzScalarKind.Int16);

        // Statement-form `??=`
        state.Statements.Add($"sNullBag.OptInt0 ??= ({i32.Name} ^ 0x1357);");

        if (!state.IssueFilter.AvoidCoalesceAssignValue || state.Swarm.Focus == FuzzSwarmFocus.CoalesceAssignValue)
        {
            // Value-consuming `??=` where the RHS is a non-nullable scalar `T`
            state.Statements.Add($"{i32.Name} = ((sNullBag.OptInt1 ??= ({i32.Name} + 11)) ^ {i32.Name});");
        }
        else
        {
            // Default mode when #1101 is avoided: statement-level `??=` followed by value-consuming `??`
            state.Statements.Add($"sNullBag.OptInt1 ??= sNullBag.OptInt0;");
            state.Statements.Add($"{i32.Name} = ((sNullBag.OptInt1 ?? {i32.Name}) + (sNullBag.OptInt0.HasValue ? 3 : 1));");
        }

        // Same-width `??` on long?
        state.Statements.Add($"{i64.Name} = ((sNullBag.OptLong0 ?? {i64.Name}) ^ (sNullBag.OptLong0.HasValue ? 7L : 0L));");

        // When #998 is allowed via --allow-issue 998, also emit mixed-width / narrow-promoted `??`
        if (!state.IssueFilter.AvoidMixedWidthNullCoalesce)
        {
            state.Statements.Add($"{i32.Name} = ((sNullBag.OptShort0 ?? {i32.Name}) ^ {i16.Name});");
        }

        // When #984 is allowed (Profile M or --allow-issue 984), also mutate and coalesce the Nullable<T> local variables
        if (!state.IssueFilter.AvoidNullableLocalVariables && state.NullableSlots.Count >= 4)
        {
            state.Statements.Add($"optLocal0 ??= {i32.Name};");
            state.Statements.Add($"{i32.Name} = ((optLocal0 ?? {i32.Name}) ^ (int)(optLocal1 ?? {i64.Name}));");
        }
        else if (rng.NextBool())
        {
            state.Statements.Add($"sNullBag.OptLong0 ??= ({i64.Name} + 9L);");
        }
    }

    /// <summary>Emits a <see cref="Span{T}"/> / <see cref="ReadOnlySpan{T}"/> statement with masked indexing and null pattern checks.</summary>
    public static void EmitSpanStatement(KernelGenerationState state)
    {
        if (state.SpanLocals.Count == 0)
        {
            return;
        }

        var rng = state.Rng;
        var i32 = state.FindLocal(FuzzScalarKind.Int32);
        var rwSpan = state.SpanLocals[0];
        var roSpan = state.SpanLocals.Count > 1 ? state.SpanLocals[1] : rwSpan;
        int mask = rwSpan.Length - 1;

        string idxExpr = rng.NextBool()
            ? $"({i32.Name} & {mask})"
            : $"((inB[gid] + {rng.NextRange(0, mask)}) & {mask})";
        string readIdxExpr = $"((inA[gid] ^ {i32.Name}) & {mask})";

        state.Statements.Add($"{rwSpan.Name}[{idxExpr}] = ({roSpan.Name}[{readIdxExpr}] + {rwSpan.Name}.Length);");
        state.Statements.Add(
            $"{i32.Name} = ((inA is not null && !(inB is null) && {rwSpan.Name}.Length > 0) ? ({roSpan.Name}[{idxExpr}] ^ {i32.Name}) : {i32.Name});");
    }

    /// <summary>Emits a <c>ref</c> ternary assignment or compound assignment respecting <c>#877</c>.</summary>
    public static void EmitRefTernaryStatement(KernelGenerationState state)
    {
        var rng = state.Rng;
        var (first, second) = state.PickDistinctPair(FuzzScalarKind.Int32, FuzzScalarKind.Int32);
        string cond = FuzzScalarExpressionEmitter.GenerateBooleanExpression(state, depth: 1, maxDepth: 2);
        string rhs = FuzzScalarExpressionEmitter.GenerateLeafExpression(state, first.Type);
        string op = rng.NextBelow(3) switch
        {
            0 => "=",
            1 => "+=",
            _ => "^=",
        };

        if (rng.Chance(0.5))
        {
            state.Statements.Add($"({cond} ? ref sPack1.MisalignedInt : ref {first.Name}) {op} {rhs};");
        }
        else if (rng.NextBool())
        {
            state.Statements.Add($"({cond} ? ref sPack4.AlignedInt : ref {first.Name}) {op} {rhs};");
        }
        else
        {
            state.Statements.Add($"({cond} ? ref {first.Name} : ref {second.Name}) {op} {rhs};");
        }
    }

    /// <summary>Emits a tuple deconstruction statement.</summary>
    public static void EmitTupleDeconstructionStatement(KernelGenerationState state)
    {
        var rng = state.Rng;
        var (first, second) = state.PickDistinctPair(FuzzScalarKind.Int32, FuzzScalarKind.Int32);

        if (!state.IssueFilter.AvoidComplexTupleDeconstruction || state.Swarm.Focus == FuzzSwarmFocus.TupleDeconstructComplex)
        {
            var third = state.FindLocal(FuzzScalarKind.Int64);
            if (rng.NextBool())
            {
                // Nested tuple deconstruction target
                state.Statements.Add($"({first.Name}, ({second.Name}, {third.Name})) = (inA[gid], (inB[gid], (long)inC[gid]));");
            }
            else
            {
                // Non-tuple-literal RHS (conditional ternary returning a value tuple)
                string cond = FuzzScalarExpressionEmitter.GenerateBooleanExpression(state, depth: 1, maxDepth: 2);
                state.Statements.Add($"({first.Name}, {second.Name}) = ({cond} ? (inA[gid], inB[gid]) : (inB[gid], inC[gid]));");
            }

            return;
        }

        string e1 = FuzzScalarExpressionEmitter.GenerateExpression(state, first.Type, depth: 1, maxDepth: 2);
        string e2 = FuzzScalarExpressionEmitter.GenerateExpression(state, second.Type, depth: 1, maxDepth: 2);
        state.Statements.Add(
            $"({first.Name}, {second.Name}) = ({FuzzScalarExpressionEmitter.EnsureType(e1, first.Type)}, {FuzzScalarExpressionEmitter.EnsureType(e2, second.Type)});");
    }

    /// <summary>Generates a C# <c>switch</c> expression over a masked expression with constant and relational patterns.</summary>
    public static string GenerateSwitchExpression(KernelGenerationState state, FuzzTypeInfo targetType, int depth, int maxDepth)
    {
        var rng = state.Rng;
        var i32 = state.FindLocal(FuzzScalarKind.Int32);
        string selector = $"(({i32.Name} ^ inC[gid]) & 7)";
        string arm0 = FuzzScalarExpressionEmitter.EnsureType(
            FuzzScalarExpressionEmitter.GenerateLeafExpression(state, targetType),
            targetType);
        string arm1 = FuzzScalarExpressionEmitter.EnsureType(
            FuzzScalarExpressionEmitter.GenerateLeafExpression(state, targetType),
            targetType);
        string arm2 = FuzzScalarExpressionEmitter.EnsureType(
            FuzzScalarExpressionEmitter.GenerateExpression(state, targetType, depth, maxDepth),
            targetType);
        string armDefault = FuzzScalarExpressionEmitter.EnsureType(
            FuzzScalarExpressionEmitter.GenerateLeafExpression(state, targetType),
            targetType);

        int cut = rng.NextRange(2, 5);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"({selector} switch {{ 0 => {arm0}, 1 => {arm1}, {cut} => {arm2}, _ => {armDefault} }})");
    }
}

