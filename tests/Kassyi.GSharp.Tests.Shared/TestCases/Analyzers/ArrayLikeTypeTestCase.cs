using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public class ArrayLikeTypeTestCase
{
    // HappyPath fields
    public int[] ArrayField;
    public Span<int> SpanField;
    public ReadOnlySpan<int> ReadOnlySpanField;
    public Memory<int> MemoryField;
    public ReadOnlyMemory<int> ReadOnlyMemoryField;
    public ArraySegment<int> ArraySegmentField;
    public ImmutableArray<int> ImmutableArrayField;

    // SadPath fields
    public int IntField;
    public List<int> ListField;
    public Fake.System.Span<int> FakeSystemSpanField;
    public Fake.Memory<int> FakeMemoryField;
}

namespace Fake.System
{
    public struct Span<T> { }
}

namespace Fake
{
    public struct Memory<T> { }
}
