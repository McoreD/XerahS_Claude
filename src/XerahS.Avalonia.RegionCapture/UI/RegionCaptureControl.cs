using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using XerahS.Avalonia.RegionCapture.Models;
using XerahS.Avalonia.RegionCapture.Services;

namespace XerahS.Avalonia.RegionCapture.UI;

/// <summary>
/// Custom control that handles region capture rendering and interaction.
/// </summary>
public sealed class RegionCaptureControl : Control
{
    private readonly MonitorInfo _monitor;
    private readonly CoordinateTranslationService _coordinateService;

    private CaptureState _state = CaptureState.Hovering;
    private PixelPoint _startPoint;
    private PixelPoint _currentPoint;
    private PixelRect _selectionRect;
    private WindowInfo? _hoveredWindow;

    // Visual settings
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0));
    private static readonly IPen SelectionPen = new Pen(Brushes.White, 2);
    private static readonly IPen WindowSnapPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 174, 255)), 3);

    public event Action<PixelRect>? RegionSelected;
    public event Action? Cancelled;

    public RegionCaptureControl(MonitorInfo monitor)
    {
        _monitor = monitor;
        _coordinateService = new CoordinateTranslationService();

        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetPosition(this);
        _startPoint = LocalToPhysical(point);
        _currentPoint = _startPoint;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (_state == CaptureState.Hovering && _hoveredWindow is not null)
            {
                // Snap to window
                _selectionRect = _hoveredWindow.SnapBounds;
                _state = CaptureState.Selected;
                ConfirmSelection();
            }
            else
            {
                _state = CaptureState.Dragging;
                _selectionRect = new PixelRect(_startPoint.X, _startPoint.Y, 0, 0);
            }

            e.Pointer.Capture(this);
            InvalidateVisual();
        }
        else if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            Cancelled?.Invoke();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var point = e.GetPosition(this);
        _currentPoint = LocalToPhysical(point);

        switch (_state)
        {
            case CaptureState.Hovering:
                UpdateHoveredWindow();
                break;

            case CaptureState.Dragging:
                _selectionRect = PixelRect.FromCorners(_startPoint, _currentPoint);
                break;
        }

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_state == CaptureState.Dragging)
        {
            e.Pointer.Capture(null);

            _selectionRect = _selectionRect.Normalize();

            if (_selectionRect.Width > 3 && _selectionRect.Height > 3)
            {
                _state = CaptureState.Selected;
                ConfirmSelection();
            }
            else
            {
                // Selection too small, go back to hovering
                _state = CaptureState.Hovering;
                _selectionRect = PixelRect.Empty;
            }

            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Cancelled?.Invoke();
            e.Handled = true;
        }
    }

    private void UpdateHoveredWindow()
    {
#if WINDOWS
        _hoveredWindow = Platform.Windows.NativeWindowService.GetWindowAtPoint(_currentPoint);
#else
        _hoveredWindow = null;
#endif
    }

    private void ConfirmSelection()
    {
        if (!_selectionRect.IsEmpty)
        {
            RegionSelected?.Invoke(_selectionRect);
        }
    }

    private PixelPoint LocalToPhysical(Point local)
    {
        // Convert from control-local logical coordinates to physical screen coordinates
        return new PixelPoint(
            local.X * _monitor.ScaleFactor + _monitor.PhysicalBounds.X,
            local.Y * _monitor.ScaleFactor + _monitor.PhysicalBounds.Y);
    }

    private Point PhysicalToLocal(PixelPoint physical)
    {
        // Convert from physical screen coordinates to control-local logical coordinates
        return new Point(
            (physical.X - _monitor.PhysicalBounds.X) / _monitor.ScaleFactor,
            (physical.Y - _monitor.PhysicalBounds.Y) / _monitor.ScaleFactor);
    }

    private Rect PhysicalRectToLocal(PixelRect rect)
    {
        var topLeft = PhysicalToLocal(rect.TopLeft);
        var bottomRight = PhysicalToLocal(rect.BottomRight);
        return new Rect(topLeft, bottomRight);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);

        // Draw dimmed background
        context.DrawRectangle(DimBrush, null, bounds);

        // Draw selection or hovered window
        Rect clearRect;

        if (_state == CaptureState.Dragging || _state == CaptureState.Selected)
        {
            if (!_selectionRect.IsEmpty)
            {
                clearRect = PhysicalRectToLocal(_selectionRect);

                // Cut out the selection area (draw with transparent)
                using (context.PushClip(clearRect))
                {
                    context.DrawRectangle(Brushes.Transparent, null, clearRect);
                }

                // Draw selection border
                context.DrawRectangle(null, SelectionPen, clearRect);

                // Draw dimensions text
                DrawDimensionsText(context, clearRect);
            }
        }
        else if (_state == CaptureState.Hovering && _hoveredWindow is not null)
        {
            clearRect = PhysicalRectToLocal(_hoveredWindow.SnapBounds);

            // Cut out the window area
            using (context.PushClip(clearRect))
            {
                context.DrawRectangle(Brushes.Transparent, null, clearRect);
            }

            // Draw window snap border
            context.DrawRectangle(null, WindowSnapPen, clearRect);
        }
    }

    private void DrawDimensionsText(DrawingContext context, Rect rect)
    {
        var text = $"{_selectionRect.Width:F0} x {_selectionRect.Height:F0}";

        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI", FontStyle.Normal, FontWeight.Normal),
            14,
            Brushes.White);

        var textX = rect.X + (rect.Width - formattedText.Width) / 2;
        var textY = rect.Bottom + 5;

        // Ensure text stays on screen
        if (textY + formattedText.Height > Bounds.Height)
            textY = rect.Top - formattedText.Height - 5;

        // Draw text background
        var textBounds = new Rect(textX - 4, textY - 2,
            formattedText.Width + 8, formattedText.Height + 4);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), null, textBounds);

        // Draw text
        context.DrawText(formattedText, new Point(textX, textY));
    }
}
