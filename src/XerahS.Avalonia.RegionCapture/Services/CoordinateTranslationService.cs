using XerahS.Avalonia.RegionCapture.Models;

namespace XerahS.Avalonia.RegionCapture.Services;

/// <summary>
/// Service for translating coordinates between logical and physical pixel spaces
/// across monitors with different DPI scaling.
/// </summary>
public sealed class CoordinateTranslationService
{
    private IReadOnlyList<MonitorInfo>? _monitors;

    /// <summary>
    /// Gets the cached list of monitors, refreshing if needed.
    /// </summary>
    public IReadOnlyList<MonitorInfo> Monitors => _monitors ??= MonitorEnumerationService.GetAllMonitors();

    /// <summary>
    /// Refreshes the monitor cache.
    /// </summary>
    public void RefreshMonitors()
    {
        _monitors = MonitorEnumerationService.GetAllMonitors();
    }

    /// <summary>
    /// Finds the monitor containing the specified physical point.
    /// </summary>
    public MonitorInfo? GetMonitorAt(PixelPoint physicalPoint)
    {
        foreach (var monitor in Monitors)
        {
            if (monitor.PhysicalBounds.Contains(physicalPoint))
                return monitor;
        }

        // Fallback: find nearest monitor
        return GetNearestMonitor(physicalPoint);
    }

    /// <summary>
    /// Finds the monitor nearest to the specified physical point.
    /// </summary>
    public MonitorInfo? GetNearestMonitor(PixelPoint physicalPoint)
    {
        MonitorInfo? nearest = null;
        double minDistance = double.MaxValue;

        foreach (var monitor in Monitors)
        {
            var center = monitor.PhysicalBounds.Center;
            var distance = physicalPoint.DistanceSquaredTo(center);

            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = monitor;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Gets the scale factor for a physical point (DPI at that location).
    /// </summary>
    public double GetScaleFactorAt(PixelPoint physicalPoint)
    {
        var monitor = GetMonitorAt(physicalPoint);
        return monitor?.ScaleFactor ?? 1.0;
    }

    /// <summary>
    /// Converts a physical rectangle to a logical rectangle on a specific monitor.
    /// </summary>
    public (double X, double Y, double Width, double Height) PhysicalToLogical(PixelRect physical, MonitorInfo monitor)
    {
        return (
            (physical.X - monitor.PhysicalBounds.X) / monitor.ScaleFactor,
            (physical.Y - monitor.PhysicalBounds.Y) / monitor.ScaleFactor,
            physical.Width / monitor.ScaleFactor,
            physical.Height / monitor.ScaleFactor);
    }

    /// <summary>
    /// Gets the combined virtual screen bounds in physical pixels.
    /// </summary>
    public PixelRect GetVirtualScreenBounds()
    {
        if (Monitors.Count == 0)
            return PixelRect.Empty;

        var result = Monitors[0].PhysicalBounds;

        for (int i = 1; i < Monitors.Count; i++)
        {
            result = result.Union(Monitors[i].PhysicalBounds);
        }

        return result;
    }
}
