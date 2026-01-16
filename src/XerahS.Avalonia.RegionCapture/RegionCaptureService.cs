using XerahS.Avalonia.RegionCapture.Models;
using XerahS.Avalonia.RegionCapture.Services;

namespace XerahS.Avalonia.RegionCapture;

/// <summary>
/// High-level service for initiating region capture operations.
/// </summary>
public sealed class RegionCaptureService
{
    /// <summary>
    /// Initiates a region capture operation and returns the selected region in physical pixels.
    /// </summary>
    /// <returns>The captured region, or null if cancelled.</returns>
    public async Task<PixelRect?> CaptureRegionAsync()
    {
        var monitors = MonitorEnumerationService.GetAllMonitors();

        if (monitors.Count == 0)
            return null;

        var tcs = new TaskCompletionSource<PixelRect?>();

        // Create overlay windows for each monitor
        var overlays = monitors.Select(m => new UI.OverlayWindow(m, tcs)).ToList();

        try
        {
            // Show all overlays
            foreach (var overlay in overlays)
            {
                overlay.Show();
            }

            // Wait for result
            return await tcs.Task;
        }
        finally
        {
            // Clean up all overlays
            foreach (var overlay in overlays)
            {
                overlay.Close();
            }
        }
    }
}
