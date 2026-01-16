using Avalonia.Controls;
using Avalonia.Interactivity;

namespace XerahS.Avalonia.RegionCapture.Demo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnCaptureClick(object? sender, RoutedEventArgs e)
    {
        var captureService = new RegionCaptureService();
        var result = await captureService.CaptureRegionAsync();

        if (result is not null)
        {
            ResultText.Text = $"Captured: X={result.Value.X}, Y={result.Value.Y}, " +
                              $"Width={result.Value.Width}, Height={result.Value.Height}";
        }
        else
        {
            ResultText.Text = "Capture cancelled.";
        }
    }
}
