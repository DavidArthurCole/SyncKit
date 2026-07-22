using Microsoft.Extensions.Options;

namespace SyncKit.Auth.Tests;

public sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T> where T : class {
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
