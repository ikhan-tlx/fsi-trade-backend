using FSI.Trade.Compliance.Application.Contracts.Identity;

namespace FSI.Trade.Compliance.Infrastructure.Services;

/// <summary>AsyncLocal-backed <see cref="IAmbientUserContext"/> (registered singleton).</summary>
public sealed class AmbientUserContext : IAmbientUserContext
{
    private readonly AsyncLocal<AmbientUser?> _current = new();

    public AmbientUser? Current => _current.Value;

    public IDisposable Impersonate(string userId, string? userName, int tenantId)
    {
        var previous = _current.Value;
        _current.Value = new AmbientUser(userId, userName, tenantId);
        return new Scope(this, previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly AmbientUserContext _owner;
        private readonly AmbientUser?       _previous;
        private bool _disposed;
        public Scope(AmbientUserContext owner, AmbientUser? previous) { _owner = owner; _previous = previous; }
        public void Dispose()
        {
            if (_disposed) return;
            _owner._current.Value = _previous;
            _disposed = true;
        }
    }
}
