using System.Diagnostics;

namespace TheOne.Avalonia.Controls;

// Optional in-process measurements for PreviewHost. No listener, allocation or
// clock/GC sampling on the normal path; no public telemetry API.
internal static class CodeWorkProbe
{
    internal static Action<Sample>? Observer;
    private static long _nextId;
    [ThreadStatic] private static long _parent;

    internal static Scope Start(string operation, CancellationToken token = default)
    {
        return Observer is { } observer ? new Scope(operation, token, observer) : default;
    }

    internal readonly record struct Sample(
        long Id,
        long Parent,
        string Operation,
        bool Starting,
        long Timestamp,
        long ElapsedTicks,
        long AllocatedBytes,
        bool Canceled,
        int Thread);

    internal readonly struct Scope : IDisposable
    {
        private readonly Action<Sample>? _observer;
        private readonly string? _operation;
        private readonly long _id, _parentId, _start, _bytes;
        private readonly CancellationToken _token;

        internal Scope(string operation, CancellationToken token, Action<Sample> observer)
        {
            _observer = observer;
            _operation = operation;
            _token = token;
            _id = Interlocked.Increment(ref _nextId);
            _parentId = _parent;
            _parent = _id;
            _start = Stopwatch.GetTimestamp();
            _bytes = GC.GetAllocatedBytesForCurrentThread();
            observer(new Sample(_id, _parentId, operation, true, _start, 0, 0, false,
                Environment.CurrentManagedThreadId));
        }

        public void Dispose()
        {
            if (_observer == null) return;
            _parent = _parentId;
            _observer(new Sample(_id, _parentId, _operation!, false, _start, Stopwatch.GetTimestamp() - _start,
                GC.GetAllocatedBytesForCurrentThread() - _bytes, _token.IsCancellationRequested,
                Environment.CurrentManagedThreadId));
        }
    }
}