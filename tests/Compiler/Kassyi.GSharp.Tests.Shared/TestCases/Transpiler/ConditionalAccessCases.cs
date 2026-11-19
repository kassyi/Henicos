#nullable enable
#pragma warning disable CS0649, CS0219
using System;

namespace ConditionalAccessCases;

internal struct ConditionalCasePoint
{
    public int X;

    public int this[int i] => X + i;
}

internal struct ConditionalCaseHolder
{
    public int[]? Data;
}

internal static class Cases
{
    public static void ArrayLength(int[]? a)
    {
        int? n = a?.Length;
    }

    public static void ArrayElement(int[]? a)
    {
        int? n = a?[1];
    }

    public static void NullableMember(ConditionalCasePoint? p)
    {
        int? n = p?.X;
    }

    public static void ElementThatIsAnArray(int[][]? jagged)
    {
        int[]? n = jagged?[0];
    }

    public static void ElementThatIsNullable(int?[]? a)
    {
        int? n = a?[0];
    }

    public static void StackArray()
    {
        int[] local = new int[4];
        int? n = local?.Length;
    }

    public static void StringReceiver(string? s)
    {
        int? n = s?.Length;
    }

    public static void AssignedElement(int[]? a)
    {
        a?[0] = 1;
    }

    public static void NullableIndexer(ConditionalCasePoint? p)
    {
        int? n = p?[1];
    }

    public static void NullableSegmentMember(ArraySegment<int>? s)
    {
        int? n = s?.Count;
    }

    public static void NullableSegmentElement(ArraySegment<int>? s)
    {
        int? n = s?[0];
    }

    public static void NullableHolderWithNestedElement(ConditionalCaseHolder? h)
    {
        int? n = h?.Data?[0];
    }
}
