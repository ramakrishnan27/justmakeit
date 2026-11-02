namespace Platform.Auth.Services;

/// <summary>
/// One small snapshot of configuration held in memory for a while. Used for the permission matrix and the
/// menus, so both follow the same rules:
/// <list type="bullet">
/// <item>It is served for <c>lifetime</c>, then read again. Many callers at once share one load.</item>
/// <item><see cref="Invalidate"/> makes the next call read again. A load that began before an
/// <see cref="Invalidate"/> is stored as already stale, so it can never be served as current: the change
/// it missed would be hidden for a whole lifetime.</item>
/// <item>A failed load is not remembered and its exception reaches the caller, and a snapshot that has
/// expired is never served in place of a failed reload.</item>
/// </list>
/// </summary>
internal sealed class SnapshotCache<T>(TimeSpan lifetime, TimeProvider time, Func<CancellationToken, Task<T>> load) where T : class
{
    private sealed record Held(T Value, DateTimeOffset LoadedAt, long Generation);

    private readonly SemaphoreSlim _load = new(1, 1);
    private volatile Held? _held;
    private long _generation;

    public void Invalidate() => Interlocked.Increment(ref _generation);

    public async Task<T> GetAsync(CancellationToken cancellationToken)
    {
        if (_held is { } current && IsFresh(current))
        {
            return current.Value;
        }

        await _load.WaitAsync(cancellationToken);
        try
        {
            // Someone may have loaded while this call waited.
            if (_held is { } loadedMeanwhile && IsFresh(loadedMeanwhile))
            {
                return loadedMeanwhile.Value;
            }

            var generation = Interlocked.Read(ref _generation);
            var value = await load(cancellationToken);
            _held = new Held(value, time.GetUtcNow(), generation);
            return value;
        }
        finally
        {
            _load.Release();
        }
    }

    private bool IsFresh(Held held) =>
        held.Generation == Interlocked.Read(ref _generation) && time.GetUtcNow() - held.LoadedAt < lifetime;
}
