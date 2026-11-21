using Trisat.Core.Models;
using Windows.Devices.Geolocation;

namespace Trisat.Win.Services;

/// <summary>
/// Native Windows Location API (WinRT Geolocator) wrapper for Mode 0 (Internal + TLE).
/// </summary>
public sealed class WindowsLocationService : IDisposable
{
    private Geolocator? _geolocator;

    public event Action<GnssPosition>? PositionUpdated;
    public event Action<string>? StatusMessage;

    public async Task StartAsync()
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed)
            {
                StatusMessage?.Invoke("Windows Location permission denied (enable in Settings → Privacy → Location).");
                return;
            }

            _geolocator = new Geolocator
            {
                DesiredAccuracy = PositionAccuracy.High,
                MovementThreshold = 2.0,
                ReportInterval = 2000
            };
            _geolocator.PositionChanged += OnPositionChanged;

            var initial = await _geolocator.GetGeopositionAsync(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(8));
            PublishGeoposition(initial);
            StatusMessage?.Invoke("Windows Location API active.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Windows Location unavailable: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (_geolocator is not null)
        {
            _geolocator.PositionChanged -= OnPositionChanged;
            _geolocator = null;
        }
    }

    private void OnPositionChanged(Geolocator sender, PositionChangedEventArgs args)
    {
        PublishGeoposition(args.Position);
    }

    private void PublishGeoposition(Geoposition pos)
    {
        var coord = pos.Coordinate.Point.Position;
        var gnssPos = new GnssPosition(
            Latitude: coord.Latitude,
            Longitude: coord.Longitude,
            AltitudeMeters: coord.Altitude,
            HorizontalAccuracyMeters: pos.Coordinate.Accuracy,
            FixDescription: "Windows Location Sensor",
            IsSimulated: false);

        PositionUpdated?.Invoke(gnssPos);
    }

    public void Dispose() => Stop();
}
