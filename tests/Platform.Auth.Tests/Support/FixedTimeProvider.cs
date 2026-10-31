namespace Platform.Auth.Tests.Support;

/// <summary>A clock a test controls: it stays where it is put until <see cref="Advance"/> moves it.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public static FixedTimeProvider Create() => new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

    public DateTimeOffset Now => _now;

    public void Advance(TimeSpan by) => _now += by;

    public override DateTimeOffset GetUtcNow() => _now;
}
