using OpsApi.Providers.Abstractions;

namespace OpsApi.Providers;

/// <summary>
/// Resolves the right <see cref="IInfraProvider"/> for a stored connection's provider key.
/// Adding a provider = register one more IInfraProvider in DI; this registry picks it up.
/// </summary>
public sealed class ProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IInfraProvider> _providers;

    public ProviderRegistry(IEnumerable<IInfraProvider> providers) =>
        _providers = providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IInfraProvider> All => _providers.Values.ToList();

    public IInfraProvider Get(string providerKey)
    {
        if (_providers.TryGetValue(providerKey, out var provider)) return provider;
        throw new ProviderException($"Unknown provider '{providerKey}'.", 400);
    }

    public bool TryGet(string providerKey, out IInfraProvider provider) =>
        _providers.TryGetValue(providerKey, out provider!);
}
