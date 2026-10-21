using System;

public class SadPath
{
    public class MyDisposable : IDisposable 
    {
        public void Dispose() {}
    }

    public MyDisposable UnsafeProp => new MyDisposable();
    
    public MyDisposable UnsafePropWithBlock 
    {
        get { return new MyDisposable(); }
    }

    public static implicit operator MyDisposable(SadPath obj) => new MyDisposable();

    public static explicit operator MyDisposable(SadPath obj) 
    {
        return new MyDisposable();
    }
}
