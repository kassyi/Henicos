public class IrSplitLowering_Snapshot
{
    // Gpu.SyncThreads(); -> written by the IR
    public void Suspend()
{
    Gpu.SyncThreads();
    return 1;
}

    // Gpu.SyncThreads(); -> written by the IR
    public void SuspendOut()
{
    Gpu.SyncThreads();
    y = 5;
    return 1;
}

    // result[0] = a[0] + Suspend(); -> written by the IR
    public void EmbeddedStatement()
{
    if (c)
    {
        var __gsharp_arg_temp_0 = a[0];
        var __gsharp_call_temp_1 = Suspend();
        result[0] = __gsharp_arg_temp_0 + __gsharp_call_temp_1;
    }
}

    // if (Suspend() > result[2]) { result[1] = 2; } -> written by the IR
    public void ElseIfConditionSplit()
{
    if (result[0] > 0)
    {
        result[1] = 1;
    }
    else
    {
        var __gsharp_call_temp_0 = Suspend();
        if (__gsharp_call_temp_0 > result[2])
        {
            result[1] = 2;
        }
    }
}

    // => a[0] + Suspend() -> written by the IR
    public void ExpressionBodied()
{
    var __gsharp_arg_temp_0 = a[0];
    var __gsharp_call_temp_1 = Suspend();
    return __gsharp_arg_temp_0 + __gsharp_call_temp_1;
}

    // result[0] = SuspendOut(out int y) + y; -> written by the IR
    public void OutDeclarationOfASplit()
{
    int y;
    var __gsharp_call_temp_0 = SuspendOut(out y);
    result[0] = __gsharp_call_temp_0 + y;
}

    // int p = Bump(a, l), q = p + Warp.ShuffleSync(0xffffffffu, x, 0); -> written by the IR
    public void LaterDeclaratorWithShuffle()
{
    int l = ThreadIndex.X;
    int p = Bump(a, l);
    var __gsharp_warp_temp_0 = Warp.ShuffleSync(0xffffffffu, x, 0);
    int q = p + __gsharp_warp_temp_0;
    result[l] = q;
}

    // result[0] = TakeLong(c ? 1u : -1, Suspend()); -> skip NoNaturalType
    public void OperandWithoutNaturalType()
{
    var __gsharp_call_temp_0 = Suspend();
    result[0] = TakeLong(c ? 1u : -1, __gsharp_call_temp_0);
}

    // result[0] = Pair(a[0], Suspend()); -> written by the IR
    public void EarlierArgumentOfACallThatDoesNotSplit()
{
    var __gsharp_arg_temp_0 = a[0];
    var __gsharp_call_temp_1 = Suspend();
    result[0] = Pair(__gsharp_arg_temp_0, __gsharp_call_temp_1);
}

    // cells[b[i]].F = Suspend(); -> written by the IR
    public void CheckOfAFieldTarget()
{
    int i = ThreadIndex.X;
    var __gsharp_arg_temp_0 = b[i];
    {
        var __gsharp_arg_temp_1 = cells[__gsharp_arg_temp_0];
    }

    var __gsharp_call_temp_2 = Suspend();
    cells[__gsharp_arg_temp_0].F = __gsharp_call_temp_2;
}

    // x += Suspend(); -> written by the IR
    public void CompoundLocal()
{
    int x = ThreadIndex.X;
    var __gsharp_call_temp_0 = Suspend();
    x += __gsharp_call_temp_0;
    result[0] = x;
}

    // Suspend(); -> written by the IR
    public void DiscardedValueOfASplit()
{
    var __gsharp_call_temp_0 = Suspend();
}

    // result[0] = a[0] + Suspend(); -> written by the IR
    // result[1] = a[1] + Suspend(); -> written by the IR
    public void TwoSplitStatements()
{
    var __gsharp_arg_temp_0 = a[0];
    var __gsharp_call_temp_1 = Suspend();
    result[0] = __gsharp_arg_temp_0 + __gsharp_call_temp_1;
    var __gsharp_arg_temp_2 = a[1];
    var __gsharp_call_temp_3 = Suspend();
    result[1] = __gsharp_arg_temp_2 + __gsharp_call_temp_3;
}

    // result[l] = a[l] + Suspend(); -> written by the IR
    // for (int k = 0; k < 2; k++) { result[k] = a[k] + Suspend(); } -> fallback OpaqueHoldsSplit
    public void MixedIrAndLoopSplits()
{
    int l = ThreadIndex.X;
    var __gsharp_arg_temp_0 = a[l];
    var __gsharp_call_temp_1 = Suspend();
    result[l] = __gsharp_arg_temp_0 + __gsharp_call_temp_1;
    for (int k = 0; k < 2; k++)
    {
        var __gsharp_arg_temp_2 = a[k];
        var __gsharp_call_temp_3 = Suspend();
        result[k] = __gsharp_arg_temp_2 + __gsharp_call_temp_3;
    }
}

    // Gpu.SyncThreads(); -> written by the IR
    public void AfterBarrier()
{
    Gpu.SyncThreads();
    return x;
}

    // Gpu.SyncThreads(); -> written by the IR
    public void Suspend()
{
    Gpu.SyncThreads();
    return 1;
}

    // Gpu.SyncThreads(); -> written by the IR
    public void SuspendWith()
{
    Gpu.SyncThreads();
    return a + b;
}

    // result[l] = (a[l] = l + 100) + Warp.ShuffleSync(0xffffffffu, a[l], 0); -> written by the IR
    public void LeftOperandWrittenBeforeShuffle()
{
    int l = ThreadIndex.X;
    var __gsharp_arg_temp_0 = a[l] = l + 100;
    var __gsharp_arg_temp_1 = a[l];
    var __gsharp_warp_temp_2 = Warp.ShuffleSync(0xffffffffu, __gsharp_arg_temp_1, 0);
    result[l] = (__gsharp_arg_temp_0) + __gsharp_warp_temp_2;
}

    // result[0] = x + Warp.ShuffleSync(0xffffffffu, y, 0); -> written by the IR
    public void LocalLeftOperandStays()
{
    var __gsharp_warp_temp_0 = Warp.ShuffleSync(0xffffffffu, y, 0);
    result[0] = x + __gsharp_warp_temp_0;
}

    // a[l] += Warp.ShuffleSync(0xffffffffu, Bump(a, l), 0); -> written by the IR
    public void CompoundElementTarget()
{
    int l = ThreadIndex.X;
    var __gsharp_arg_temp_0 = a[l];
    var __gsharp_arg_temp_1 = Bump(a, l);
    var __gsharp_warp_temp_2 = Warp.ShuffleSync(0xffffffffu, __gsharp_arg_temp_1, 0);
    __gsharp_arg_temp_0 += __gsharp_warp_temp_2;
    a[l] = __gsharp_arg_temp_0;
}

    // x += Warp.ShuffleSync(0xffffffffu, Inc(ref x), 0); -> written by the IR
    public void CompoundLocalTarget()
{
    int x = ThreadIndex.X;
    var __gsharp_arg_temp_0 = x;
    var __gsharp_arg_temp_1 = Inc(ref x);
    var __gsharp_warp_temp_2 = Warp.ShuffleSync(0xffffffffu, __gsharp_arg_temp_1, 0);
    __gsharp_arg_temp_0 += __gsharp_warp_temp_2;
    x = __gsharp_arg_temp_0;
    result[0] = x;
}

    // result[0] = Pair(a[0], Suspend()); -> written by the IR
    public void EarlierArgumentOfACallThatDoesNotSplit()
{
    var __gsharp_arg_temp_0 = a[0];
    var __gsharp_call_temp_1 = Suspend();
    result[0] = Pair(__gsharp_arg_temp_0, __gsharp_call_temp_1);
}

    // result[0] = SuspendWith(a[0], c ? Suspend() : 0); -> written by the IR
    public void ArgumentLeftOfAConditionalSplit()
{
    var __gsharp_arg_temp_0 = a[0];
    var __gsharp_cond_temp_0 = default;
    if (c)
    {
        var __gsharp_call_temp_1 = Suspend();
        __gsharp_cond_temp_0 = __gsharp_call_temp_1;
    }
    else
    {
        __gsharp_cond_temp_0 = 0;
    }

    var __gsharp_call_temp_2 = SuspendWith(__gsharp_arg_temp_0, __gsharp_cond_temp_0);
    result[0] = __gsharp_call_temp_2;
}

    // bool p = tid > 0, q = p && Suspend() > 0; -> written by the IR
    public void LaterDeclarator()
{
    int tid = ThreadIndex.X;
    bool p = tid > 0;
    var __gsharp_cond_temp_0 = p;
    if (__gsharp_cond_temp_0)
    {
        var __gsharp_call_temp_0 = Suspend();
        __gsharp_cond_temp_0 = __gsharp_call_temp_0 > 0;
    }

    bool q = __gsharp_cond_temp_0;
    result[tid] = q ? 1 : 0;
}

    // result[0] = SuspendWith(b: Inc(ref i), a: i); -> written by the IR
    public void NamedArgumentsInWrittenOrder()
{
    int i = ThreadIndex.X;
    var __gsharp_arg_temp_0 = Inc(ref i);
    var __gsharp_arg_temp_1 = i;
    var __gsharp_call_temp_2 = SuspendWith(b: __gsharp_arg_temp_0, a: __gsharp_arg_temp_1);
    result[0] = __gsharp_call_temp_2;
}

    // result[0] = Use(span, Suspend()); -> written by the IR
    public void SpanLocalStays()
{
    Span<int> span = data.AsSpan();
    var __gsharp_call_temp_0 = Suspend();
    result[0] = Use(span, __gsharp_call_temp_0);
}

    // result[0] = Use(span.Slice(1), Suspend()); -> fallback RefLikeOperand
    public void SpanValueBeforeSplit()
{
    Span<int> span = data.AsSpan();
    var __gsharp_call_temp_0 = Suspend();
    result[0] = Use(span.Slice(1), __gsharp_call_temp_0);
}

    // Gpu.SyncThreads(); -> written by the IR
    public void UseAfterBarrier()
{
    Gpu.SyncThreads();
    return span[0] + b;
}

    // result[0] = UseAfterBarrier(span, Reassign(ref span, data)); -> fallback RefLikeOperand
    public void SpanLocalWrittenByALaterArgument()
{
    Span<int> span = data.AsSpan();
    var __gsharp_arg_temp_0 = span;
    var __gsharp_arg_temp_1 = Reassign(ref span, data);
    var __gsharp_call_temp_2 = UseAfterBarrier(__gsharp_arg_temp_0, __gsharp_arg_temp_1);
    result[0] = __gsharp_call_temp_2;
}

    // result[0] = a[0] + (c ? Suspend() : 0); -> written by the IR
    public void LeftOperandOfAConditionalSplit()
{
    var __gsharp_arg_temp_0 = a[0];
    var __gsharp_cond_temp_0 = default;
    if (c)
    {
        var __gsharp_call_temp_1 = Suspend();
        __gsharp_cond_temp_0 = __gsharp_call_temp_1;
    }
    else
    {
        __gsharp_cond_temp_0 = 0;
    }

    result[0] = __gsharp_arg_temp_0 + (__gsharp_cond_temp_0);
}

    // r[t + 32] = Warp.ActiveMask(); -> written by the IR
    public void TargetIndexOverLocals()
{
    int t = ThreadIndex.X;
    var __gsharp_warp_temp_0 = Warp.ActiveMask();
    r[t + 32] = __gsharp_warp_temp_0;
}

    // r[t / u] = Suspend(); -> written by the IR
    public void TargetIndexThatCanThrow()
{
    int t = ThreadIndex.X;
    var __gsharp_arg_temp_0 = t / u;
    var __gsharp_call_temp_1 = Suspend();
    r[__gsharp_arg_temp_0] = __gsharp_call_temp_1;
}

    // r[t + 1] = SuspendWith(Inc(ref t), 0); -> written by the IR
    public void TargetIndexWrittenByTheValue()
{
    int t = ThreadIndex.X;
    var __gsharp_arg_temp_0 = t + 1;
    var __gsharp_arg_temp_1 = Inc(ref t);
    var __gsharp_call_temp_2 = SuspendWith(__gsharp_arg_temp_1, 0);
    r[__gsharp_arg_temp_0] = __gsharp_call_temp_2;
}

    // cells[b[i]].F = Suspend(); -> written by the IR
    public void StructElementFieldTarget()
{
    int i = ThreadIndex.X;
    var __gsharp_arg_temp_0 = b[i];
    {
        var __gsharp_arg_temp_1 = cells[__gsharp_arg_temp_0];
    }

    var __gsharp_call_temp_2 = Suspend();
    cells[__gsharp_arg_temp_0].F = __gsharp_call_temp_2;
}

    // result[0] = Store(ref a[b[i]], Suspend()); -> written by the IR
    public void RefElementArgumentOfACallThatDoesNotSplit()
{
    int i = ThreadIndex.X;
    var __gsharp_arg_temp_0 = b[i];
    {
        var __gsharp_arg_temp_1 = a[__gsharp_arg_temp_0];
    }

    var __gsharp_call_temp_2 = Suspend();
    result[0] = Store(ref a[__gsharp_arg_temp_0], __gsharp_call_temp_2);
}

    // result[0] = Triple(a[0], Warp.ShuffleSync(0xffffffffu, x, 0), c ? Suspend() : 0); -> written by the IR
    public void EarlierOperandBeforeAnUnconditionalSplit()
{
    var __gsharp_arg_temp_0 = a[0];
    var __gsharp_warp_temp_1 = Warp.ShuffleSync(0xffffffffu, x, 0);
    var __gsharp_cond_temp_0 = default;
    if (c)
    {
        var __gsharp_call_temp_2 = Suspend();
        __gsharp_cond_temp_0 = __gsharp_call_temp_2;
    }
    else
    {
        __gsharp_cond_temp_0 = 0;
    }

    result[0] = Triple(__gsharp_arg_temp_0, __gsharp_warp_temp_1, __gsharp_cond_temp_0);
}

    // Gpu.SyncThreads(); -> written by the IR
    public void SuspendRef()
{
    Gpu.SyncThreads();
    target = value;
}

    // s[i] = Suspend(); -> written by the IR
    public void SpanElementTarget()
{
    Span<int> s = data.AsSpan();
    int i = ThreadIndex.X;
    {
        var __gsharp_arg_temp_0 = s[i];
    }

    var __gsharp_call_temp_1 = Suspend();
    s[i] = __gsharp_call_temp_1;
}

    // a[i] = Suspend(); -> written by the IR
    public void ElementTarget()
{
    int i = ThreadIndex.X;
    var __gsharp_call_temp_0 = Suspend();
    a[i] = __gsharp_call_temp_0;
}

    // result[0] = cells[i].Add(Suspend()); -> written by the IR
    public void StructMethodOfElement()
{
    int i = ThreadIndex.X;
    {
        var __gsharp_arg_temp_0 = cells[i];
    }

    var __gsharp_call_temp_1 = Suspend();
    result[0] = cells[i].Add(__gsharp_call_temp_1);
}

    // SuspendRef(ref a[i], 1); -> written by the IR
    public void RefElementOfASplit()
{
    int i = ThreadIndex.X;
    {
        var __gsharp_arg_temp_0 = a[i];
    }

    SuspendRef(ref a[i], 1);
}

    // r[l] = Triple(a[l] += Warp.ShuffleSync(0xffffffffu, x, 0), a[l], Warp.ShuffleSync(0xffffffffu, y, 0)); -> written by the IR
    public void CompoundThenReadSameElement()
{
    int l = ThreadIndex.X;
    var __gsharp_arg_temp_0 = a[l];
    var __gsharp_warp_temp_1 = Warp.ShuffleSync(0xffffffffu, x, 0);
    __gsharp_arg_temp_0 += __gsharp_warp_temp_1;
    a[l] = __gsharp_arg_temp_0;
    var __gsharp_arg_temp_2 = a[l];
    var __gsharp_warp_temp_3 = Warp.ShuffleSync(0xffffffffu, y, 0);
    r[l] = Triple(__gsharp_arg_temp_0, __gsharp_arg_temp_2, __gsharp_warp_temp_3);
}

    // a[l] += b[l] += Warp.ShuffleSync(0xffffffffu, x, 0); -> written by the IR
    public void NestedCompound()
{
    int l = ThreadIndex.X;
    var __gsharp_arg_temp_0 = a[l];
    var __gsharp_arg_temp_1 = b[l];
    var __gsharp_warp_temp_2 = Warp.ShuffleSync(0xffffffffu, x, 0);
    __gsharp_arg_temp_1 += __gsharp_warp_temp_2;
    b[l] = __gsharp_arg_temp_1;
    __gsharp_arg_temp_0 += __gsharp_arg_temp_1;
    a[l] = __gsharp_arg_temp_0;
}

    // cells[i].F += Suspend(); -> written by the IR
    public void CompoundFieldOfElement()
{
    int i = ThreadIndex.X;
    var __gsharp_arg_temp_0 = cells[i].F;
    var __gsharp_call_temp_1 = Suspend();
    __gsharp_arg_temp_0 += __gsharp_call_temp_1;
    cells[i].F = __gsharp_arg_temp_0;
}

    // => c.Add(Suspend()) -> fallback ReadOnlyVariableReceiver
    public void ReadOnlyReceiverOfAMutatingMethod()
{
    var __gsharp_call_temp_0 = Suspend();
    return c.Add(__gsharp_call_temp_0);
}

    // => c.Peek(Suspend()) -> written by the IR
    public void ReadOnlyReceiverOfAReadOnlyMethod()
{
    var __gsharp_call_temp_0 = Suspend();
    return c.Peek(__gsharp_call_temp_0);
}

    // r[(int)n] = Suspend(); -> written by the IR
    public void IndexNumericConversion()
{
    var __gsharp_call_temp_0 = Suspend();
    r[(int)n] = __gsharp_call_temp_0;
}

    // r[(int)lane] = Suspend(); -> written by the IR
    public void IndexEnumConversion()
{
    var __gsharp_call_temp_0 = Suspend();
    r[(int)lane] = __gsharp_call_temp_0;
}

    // r[ThreadIndex.X + 1] = Suspend(); -> written by the IR
    public void IndexThreadIndex()
{
    var __gsharp_call_temp_0 = Suspend();
    r[ThreadIndex.X + 1] = __gsharp_call_temp_0;
}

    // r[(int)n] = Suspend(); -> written by the IR
    public void IndexNullableConversion()
{
    var __gsharp_arg_temp_0 = (int)n;
    var __gsharp_call_temp_1 = Suspend();
    r[__gsharp_arg_temp_0] = __gsharp_call_temp_1;
}

    // r[(int)o] = Suspend(); -> written by the IR
    public void IndexUnboxing()
{
    var __gsharp_arg_temp_0 = (int)o;
    var __gsharp_call_temp_1 = Suspend();
    r[__gsharp_arg_temp_0] = __gsharp_call_temp_1;
}

    // r[checked(t + u)] = Suspend(); -> skip OverflowContextOperand
    public void IndexCheckedArithmetic()
{
    int t = ThreadIndex.X;
    var __gsharp_call_temp_0 = Suspend();
    r[checked(t + u)] = __gsharp_call_temp_0;
}

    // r[idx[0]] = Suspend(); -> written by the IR
    public void IndexArrayRead()
{
    var __gsharp_arg_temp_0 = idx[0];
    var __gsharp_call_temp_1 = Suspend();
    r[__gsharp_arg_temp_0] = __gsharp_call_temp_1;
}

    // data[t] = cached[t] ?? Warp.ShuffleSync(0xffffffffu, data[t], 0); -> skip CoalesceHoldsSplit
    public void CoalesceRight()
{
    int t = ThreadIndex.X;
    var __gsharp_arg_temp_0 = data[t];
    var __gsharp_warp_temp_1 = Warp.ShuffleSync(0xffffffffu, __gsharp_arg_temp_0, 0);
    data[t] = cached[t] ?? __gsharp_warp_temp_1;
}

    // data[0] = (x - 1).AfterBarrier(); -> skip SplitExtensionReceiver
    public void ExtensionReceiverOfASplit()
{
    int x = ThreadIndex.X;
    var __gsharp_call_temp_0 = (x - 1).AfterBarrier();
    data[0] = __gsharp_call_temp_0;
}

    // result[0] = Take(a[0], Suspend()); -> skip UserDefinedImplicitConversionSpill
    public void UserDefinedConversionBeforeASplit()
{
    var __gsharp_call_temp_0 = Suspend();
    result[0] = Take(a[0], __gsharp_call_temp_0);
}

    // bool q = p || Suspend() > 0; -> written by the IR
    public void OrElseDeclarator()
{
    bool p = ThreadIndex.X > 0;
    var __gsharp_cond_temp_0 = p;
    if (!__gsharp_cond_temp_0)
    {
        var __gsharp_call_temp_0 = Suspend();
        __gsharp_cond_temp_0 = __gsharp_call_temp_0 > 0;
    }

    bool q = __gsharp_cond_temp_0;
    result[0] = q ? 1 : 0;
}

    // Suspend(); -> written by the IR
    public void DiscardedSuspend()
{
    var __gsharp_call_temp_0 = Suspend();
}

    // Gpu.SyncThreads(); -> written by the IR
    public void BarrierStatement()
{
    Gpu.SyncThreads();
}

    // Warp.ShuffleSync(0xffffffffu, x, 0); -> written by the IR
    public void DiscardedShuffle()
{
    Warp.ShuffleSync(0xffffffffu, x, 0);
}

    // if (Suspend() > 0) { result[0] = Suspend(); } -> written by the IR
    // result[0] = Suspend(); -> written by the IR
    public void IfConditionSplit()
{
    var __gsharp_call_temp_0 = Suspend();
    if (__gsharp_call_temp_0 > 0)
    {
        var __gsharp_call_temp_1 = Suspend();
        result[0] = __gsharp_call_temp_1;
    }
}

    // return a[0] + Suspend(); -> written by the IR
    public void ReturnAfterSplit()
{
    var __gsharp_arg_temp_0 = a[0];
    var __gsharp_call_temp_1 = Suspend();
    return __gsharp_arg_temp_0 + __gsharp_call_temp_1;
}

    // result[0] = c ? Suspend() : Warp.ShuffleSync(0xffffffffu, x, 0); -> written by the IR
    public void ConditionalBothArmsSplit()
{
    var __gsharp_cond_temp_0 = default;
    if (c)
    {
        var __gsharp_call_temp_0 = Suspend();
        __gsharp_cond_temp_0 = __gsharp_call_temp_0;
    }
    else
    {
        var __gsharp_warp_temp_1 = Warp.ShuffleSync(0xffffffffu, x, 0);
        __gsharp_cond_temp_0 = __gsharp_warp_temp_1;
    }

    result[0] = __gsharp_cond_temp_0;
}

    // cells[b[0]].F = Suspend(); -> written by the IR
    public void CheckReadsTheIndexOnce()
{
    var __gsharp_arg_temp_0 = b[0];
    {
        var __gsharp_arg_temp_1 = cells[__gsharp_arg_temp_0];
    }

    var __gsharp_call_temp_2 = Suspend();
    cells[__gsharp_arg_temp_0].F = __gsharp_call_temp_2;
}
}
