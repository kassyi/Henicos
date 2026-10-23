namespace Names
{
    public struct Outer<T>
    {
        public struct Inner { }
        public struct GenericInner<U> { }
    }

    public struct Plain
    {
        public struct Nested { }
    }

    public class Holder
    {
        public Outer<int>.Inner NestedInGeneric;
        public Outer<int>.GenericInner<float> GenericNestedInGeneric;
        public Outer<int>.Inner[] ArrayOfNestedInGeneric;
        public Plain.Nested NestedInPlain;
        public int? NullableInt;
    }
}
