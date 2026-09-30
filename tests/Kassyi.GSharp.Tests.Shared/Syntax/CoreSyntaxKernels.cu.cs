// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct Vector2
{
    public float X;
    public float Y;
}

public struct MixedPaddingStruct
{
    public byte Flag1;
    public double ValueD;
    public int ValueI;
    public short Flag2;
}

public static partial class CoreSyntaxKernels
{
    // SYN-90: 複雑な制御フロー (if-else ネストとループの複合)
    [GpuKernel]
    public static void ComplexLogicKernel(int[] a, int[] b, int[] c)
    {
        for (int i = 0; i < 5; i++)
        {
            if (a[0] > 10)
            {
                c[0] = c[0] + a[0];
            }
            else
            {
                c[0] = c[0] + b[0];
            }
        }
    }

    // SYN-91: 基本制御構文 (switch, do-while, 論理演算)
    [GpuKernel]
    public static void ControlFlowKernel(int[] data)
    {
        int tid = ThreadIndex.X;

        // 1. switch
        switch (tid)
        {
            case 0:
                data[tid] = 10;
                break;
            case 1:
            case 2:
                data[tid] = 20;
                break;
            default:
                data[tid] = 30;
                break;
        }

        // 2. do-while
        int count = 0;
        do
        {
            count = count + 1;
        } while (count < 3);

        data[tid] = data[tid] + count;

        // 3. && and ||
        if (tid == 0 && count == 3)
        {
            data[tid] = data[tid] + 100;
        }

        if (tid == 1 || tid == 2)
        {
            data[tid] = data[tid] + 200;
        }
    }

    // SYN-92: ユーザー定義構造体 (struct) のカーネル引数および操作
    [GpuKernel]
    public static void StructKernel(Vector2[] vectors, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            Vector2 v = vectors[tid];
            v.X = v.X * 2.0f;
            v.Y = v.Y + 1.0f;
            vectors[tid] = v;
        }
    }

    [GpuKernel]
    public static void PaddedStructKernel(MixedPaddingStruct[] items, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            MixedPaddingStruct item = items[tid];
            item.Flag1 = (byte)(item.Flag1 + 1);
            item.ValueD = item.ValueD * 1.5;
            item.ValueI = item.ValueI + 100;
            item.Flag2 = (short)(item.Flag2 - 5);
            items[tid] = item;
        }
    }

    // SYN-93: デバイスメソッド ([GpuDeviceFunction]) の呼び出し・再利用
    [GpuDeviceFunction]
    public static int MyCustomAdd(int a, int b)
    {
        return a + b;
    }

    [GpuKernel]
    public static void DeviceMethodKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = MyCustomAdd(data[tid], 100);
    }
}
