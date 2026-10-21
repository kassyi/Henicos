using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public enum PacketColor
{
    Red = 0,
    Green = 1,
    Blue = 2
}

[Flags]
public enum PacketFlags : uint
{
    None = 0u,
    Active = 1u,
    Verified = 2u
}

public struct PacketItem
{
    public PacketColor Color;
    public PacketFlags Flags;
    public int Payload;
}

public static partial class EnumTypeKernels
{
    [GpuDeviceFunction]
    public static int ScoreColor(PacketColor c) => c == PacketColor.Green ? 10 : (c == PacketColor.Blue ? 20 : 1);

    [GpuKernel]
    public static void EvaluatePacketsKernel(PacketItem[] items, PacketColor bonusColor, int[] output)
    {
        int tid = ThreadIndex.X;
        PacketItem item = items[tid];
        PacketColor localColor = item.Color;
        PacketFlags localFlags = item.Flags;

        if ((localFlags & PacketFlags.Active) != PacketFlags.None)
        {
            output[tid] = ScoreColor(localColor) + (localColor == bonusColor ? 100 : 0) + item.Payload;
        }
        else
        {
            output[tid] = 0;
        }
    }
}
