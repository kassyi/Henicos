using System;
using System.Diagnostics;

namespace Cases;

public class FakeLogger
{
    public void Info(string message) { }

    public void LogError(string message) { }

    public void Flush() { }
}

public static class Logger
{
    public static FakeLogger Instance { get; } = new();
}

public class SwallowedCases
{
    private readonly FakeLogger _logger = new();
    private readonly FakeLogger _sink = new();

    public void EmptyTyped()
    {
        try { Work(); }
        catch (InvalidOperationException) { }
    }

    public void EmptyBare()
    {
        try
        {
            Work();
        }
        catch
        {
        }
    }

    public void CommentOnly()
    {
        try { Work(); }
        catch (Exception)
        {
            // intentionally ignored
        }
    }

    public void OnlyEmptyStatement()
    {
        try { Work(); }
        catch { ; }
    }

    public void FilteredAndEmpty(bool recoverable)
    {
        try { Work(); }
        catch (Exception) when (recoverable) { }
    }

    public void ConsoleOnly()
    {
        try { Work(); }
        catch (Exception e) { Console.Error.WriteLine(e); }
    }

    public void DebugAndTrace()
    {
        try { Work(); }
        catch
        {
            Debug.WriteLine("failed");
            Trace.TraceError("failed");
        }
    }

    public void LoggerMember()
    {
        try { Work(); }
        catch (Exception e) { _logger.Info(e.Message); }
    }

    public void LogMethodName()
    {
        try { Work(); }
        catch { LogProblem(); }
    }

    public void Rethrows()
    {
        try { Work(); }
        catch (Exception) { Console.WriteLine("failed"); throw; }
    }

    public int ReturnsFallback()
    {
        try { Work(); return 1; }
        catch { return -1; }
    }

    public bool AssignsFlag()
    {
        var failed = false;
        try { Work(); }
        catch { failed = true; }
        return failed;
    }

    public void CallsOther()
    {
        try { Work(); }
        catch (Exception e) { Cleanup(e); }
    }

    public void LogsThenAssigns()
    {
        var ok = true;
        try { Work(); }
        catch { Console.WriteLine("failed"); ok = false; }
        Console.WriteLine(ok);
    }

    public void NameOnlyStartsWithLog()
    {
        try { Work(); }
        catch { Logarithm(); }
    }

    public Action InsideLambda() => () =>
    {
        try { Work(); }
        catch { }
    };

    public void CapitalLoggerReceiver()
    {
        try { Work(); }
        catch (Exception e) { Logger.Instance.Info(e.Message); }
    }

    public void ConditionalLog()
    {
        try { Work(); }
        catch (Exception e) { _logger?.Info(e.Message); }
    }

    public void ConditionalLogByMethodName()
    {
        try { Work(); }
        catch (Exception e) { _sink?.LogError(e.Message); }
    }

    public void ConditionalOtherCall()
    {
        try { Work(); }
        catch { _sink?.Flush(); }
    }

    public void NestedEmptyBlock()
    {
        try { Work(); }
        catch { { } }
    }

    public void OnlyReturns()
    {
        try { Work(); }
        catch { return; }
    }

    private static void Work() { }

    private static void LogProblem() { }

    private static void Logarithm() { }

    private static void Cleanup(Exception e) { }
}
