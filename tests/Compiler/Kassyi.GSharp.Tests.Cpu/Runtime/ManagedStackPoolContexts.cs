#nullable enable
using System;
using System.Threading;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

/// <summary>
/// The synchronization contexts that <see cref="ManagedStackPoolTests"/> set on the launching thread: each asks for
/// wait notification, so that the scheduler's waits for its GPU threads go through it.
/// </summary>
internal static class ManagedStackPoolContexts
{
    // Asks for wait notification and records that its Wait was entered
    internal abstract class EnteredWaitContext : SynchronizationContext
    {
        private int _entered;

        protected EnteredWaitContext() => SetWaitNotificationRequired();

        public bool Entered => Volatile.Read(ref _entered) != 0;

        protected void MarkEntered() => Volatile.Write(ref _entered, 1);
    }

    // Fails a wait as <mode> says: the first wait runs <inner> (which throws) before or
    // after it waits, or every wait throws WaitFailure
    internal sealed class ThrowingContext : EnteredWaitContext
    {
        public const string WaitFailure = "the wait of the context failed";

        private readonly ManagedStackPoolTests.WaitFailureMode _mode;
        private readonly Action _inner;
        private bool _ranInner;

        public ThrowingContext(ManagedStackPoolTests.WaitFailureMode mode, Action inner)
        {
            _mode = mode;
            _inner = inner;
        }

        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            MarkEntered();
            if (_mode == ManagedStackPoolTests.WaitFailureMode.EveryWait)
            {
                throw new InvalidOperationException(WaitFailure);
            }

            if (_ranInner)
            {
                return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
            }

            _ranInner = true;
            if (_mode == ManagedStackPoolTests.WaitFailureMode.InnerLaunchBeforeTheWait)
            {
                _inner();
                return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
            }

            int result = WaitHelper(waitHandles, waitAll, millisecondsTimeout);
            _inner();
            return result;
        }
    }

    // Asks for wait notification and counts the waits that an interruption ended
    internal sealed class FailedWaitContext : SynchronizationContext
    {
        private int _failedWaits;

        public FailedWaitContext() => SetWaitNotificationRequired();

        public int FailedWaits => Volatile.Read(ref _failedWaits);

        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            try
            {
                return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
            }
            catch (ThreadInterruptedException)
            {
                Interlocked.Increment(ref _failedWaits);
                throw;
            }
        }
    }

    // Asks for wait notification and counts the waits that it entered and those that it left; once armed, every wait
    // throws ArmedFailure instead of waiting
    internal sealed class CountingWaitContext : SynchronizationContext
    {
        public const string ArmedFailure = "the armed wait of the context failed";

        private int _enteredWaits;
        private int _leftWaits;
        private int _armed;

        public CountingWaitContext() => SetWaitNotificationRequired();

        public int EnteredWaits => Volatile.Read(ref _enteredWaits);

        public int LeftWaits => Volatile.Read(ref _leftWaits);

        public bool InWait => EnteredWaits > LeftWaits;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            Interlocked.Increment(ref _enteredWaits);
            try
            {
                if (Volatile.Read(ref _armed) != 0)
                {
                    throw new InvalidOperationException(ArmedFailure);
                }

                return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
            }
            finally
            {
                Interlocked.Increment(ref _leftWaits);
            }
        }
    }

    // Asks for wait notification, as the WPF dispatcher does, and runs <inner> inside the first wait
    internal sealed class ReentrantContext : EnteredWaitContext
    {
        private readonly Action _inner;

        public ReentrantContext(Action inner)
        {
            _inner = inner;
        }

        public bool RanInner { get; private set; }

        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            MarkEntered();
            if (!RanInner)
            {
                RanInner = true;
                _inner();
            }

            return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
        }
    }
}
