using System;

public class HappyPath
{
    public class NonDisposable {}

    public NonDisposable SafeProp => new NonDisposable();

    public NonDisposable SafePropWithBlock
    {
        get { return new NonDisposable(); }
    }

    public static implicit operator NonDisposable(HappyPath obj) => new NonDisposable();
    
    public static explicit operator NonDisposable(HappyPath obj)
    {
        return new NonDisposable();
    }
}
