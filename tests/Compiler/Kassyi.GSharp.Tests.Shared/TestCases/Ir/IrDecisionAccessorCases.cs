namespace IrDecisionAccessorCases;

public struct Counter
{
    private int _v;
    public int V { get => _v; set => _v = value; }
    public int Auto { get; set; }
    public readonly int Twice => _v * 2;
    public int Bump { get { _v++; return _v; } }
    public int W { get { _v++; return _v; } readonly set { } }
    public int this[int i] => _v + i;
    public static int Zero => 0;
}

public struct Holder
{
    public readonly Counter Inner;
    public Counter Loose;
}

public struct CtorHolder
{
    public readonly Counter Ro;

    public CtorHolder(CtorHolder other, int[] r)
    {
        Ro = default;
        r[0] = Ro.Bump;
        r[1] = other.Ro.Bump;
    }

    public void ReadInMethod(int[] r) { r[0] = Ro.Bump; }
}

public static class Shared
{
    private static Counter s_counter;
    public static ref readonly Counter Counter => ref s_counter;
}

public static class Accessors
{
    public static void ReadGetter(Counter c, int[] r) { r[0] = c.V; }
    public static void ReadAutoProperty(Counter c, int[] r) { r[0] = c.Auto; }
    public static void ReadBumpOfIn(in Counter c, int[] r) { r[0] = c.Bump; }
    public static void ReadBumpOfValue(Counter c, int[] r) { r[0] = c.Bump; }
    public static void ReadTwiceOfIn(in Counter c, int[] r) { r[0] = c.Twice; }
    public static void StoreSetter(Counter c) { c.V = 3; }
    public static void StoreAuto(Counter c) { c.Auto = 3; }
    public static void StoreSetterWithValueUse(Counter c, int[] r) { r[0] = c.V = 3; }
    public static void CompoundOfSetter(Counter c) { c.V += 1; }
    public static void StoreElement(int[] a, int i) { a[i] = 3; }
    public static void ReadBumpOfRefReadonlyLocal(in Counter c, int[] r)
    { ref readonly Counter l = ref c; r[0] = l.Bump; }
    public static void ReadBumpOfReadonlyFieldOfValue(Holder h, int[] r) { r[0] = h.Inner.Bump; }
    public static void ReadBumpOfLooseFieldOfIn(in Holder h, int[] r) { r[0] = h.Loose.Bump; }
    public static void ReadBumpOfLooseFieldOfValue(Holder h, int[] r) { r[0] = h.Loose.Bump; }
    public static void ReadBumpOfRefReadonlyReturn(Holder h, int[] r) { r[0] = Pick(h).Bump; }
    public static void ReadBumpOfRefReadonlyProperty(int[] r) { r[0] = Shared.Counter.Bump; }
    public static void StoreReadonlySetterOfIn(in Counter c) { c.W = 3; }
    public static void CompoundOfReadonlySetterOfIn(in Counter c) { c.W += 1; }
    public static void IncrementOfReadonlySetterOfIn(in Counter c) { c.W++; }
    public static void InitializerSetter(int[] r) { var c = new Counter { V = 3 }; r[0] = c.V; }
    public static void ReadIndexer(Counter c, int[] r) { r[0] = c[1]; }
    public static void ReadStaticProperty(int[] r) { r[0] = Counter.Zero; }

    private static ref readonly Counter Pick(in Holder h) => ref h.Inner;
}
