using SentimentAI.Core.Providers;

namespace SentimentAI.App.Services;

/// <summary>MAUI Connectivity üzerinden internet durumunu bildirir ve değişiklikleri yayınlar.</summary>
public sealed class MauiNetworkStatus : INetworkStatus, IDisposable
{
    public MauiNetworkStatus()
    {
        Connectivity.Current.ConnectivityChanged += OnChanged;
    }

    public event EventHandler<bool>? Changed;

    public bool HasInternet => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    private void OnChanged(object? sender, ConnectivityChangedEventArgs e) =>
        Changed?.Invoke(this, e.NetworkAccess == NetworkAccess.Internet);

    public void Dispose() => Connectivity.Current.ConnectivityChanged -= OnChanged;
}
