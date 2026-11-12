namespace Names
{
    public struct Outer<T>
    {
        public struct Inner { }
        public struct GenericInner<U> { }
    }

    public struct @class<T> { }

    public struct @struct { }

    public struct Plain
    {
        public struct Nested { }
    }

    public struct KeywordParameters<@in, U> { }

    public class Holder
    {
        public Outer<int>.Inner NestedInGeneric;
        public Outer<int>.GenericInner<float> GenericNestedInGeneric;
        public Outer<int>.Inner[] ArrayOfNestedInGeneric;
        public Plain.Nested NestedInPlain;
        public int? NullableInt;
        public @class<int> KeywordGeneric;
        public @class<@struct> KeywordArgument;
        public global::@event.Plain KeywordNamespace;
    }
}

namespace @event
{
    public struct Plain { }
}

namespace @event.@base
{
    public struct Deep { }
}
