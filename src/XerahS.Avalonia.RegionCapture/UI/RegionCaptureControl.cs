using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using XerahS.Avalonia.RegionCapture.Models;
using XerahS.Avalonia.RegionCapture.Services;

namespace XerahS.Avalonia.RegionCapture.UI;

/// <summary>
/// Custom control that handles region capture rendering and interaction.
/// Uses composited rendering with XOR-style selection cutout.
/// </summary>
public sealed class RegionCaptureControl : Control
{
    private readonly MonitorInfo _monitor;
    private readonly CoordinateTranslationService _coordinateService;
    private readonly WindowDetectionService _windowService;
    private readonly MagnifierControl _magnifier;

    private CaptureState _state = CaptureState.Hovering;
    private PixelPoint _startPoint;
    private PixelPoint _currentPoint;
    private PixelRect _selectionRect;
    private WindowInfo? _hoveredWindow;

    // Rendering configuration
    private readonly double _dimOpacity;
    private readonly bool _enableWindowSnapping;
    private readonly bool _enableMagnifier;

    // Visual brushes and pens (lazy initialization for performance)
    private IBrush? _dimBrush;
    private IBrush DimBrush => _dimBrush ??= new SolidColorBrush(Color.FromArgb((byte)(_dimOpacity * 255), 0, 0, 0));

    private static readonly IPen SelectionPen = new Pen(Brushes.White, 2);
    private static readonly IPen SelectionShadowPen = new Pen(new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)), 4);
    private static readonly IPen WindowSnapPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 174, 255)), 3);
    private static readonly IPen WindowSnapShadowPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 0, 174, 255)), 6);
    private static readonly IBrush InfoBackgroundBrush = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30));

    public event Action<PixelRect>? RegionSelected;
    public event Action? Cancelled;

    public RegionCaptureControl(MonitorInfo monitor, RegionCaptureOptions? options = null)
    {
        options ??= new RegionCaptureOptions();

        _monitor = monitor;
        _coordinateService = new CoordinateTranslationService();
        _windowService = new WindowDetectionService();

        _dimOpacity = options.DimOpacity;
        _enableWindowSnapping = options.EnableWindowSnapping;
        _enableMagnifier = options.EnableMagnifier;

        // Create magnifier if enabled
        _magnifier = new MagnifierControl(options.MagnifierZoom);
        _magnifier.IsVisible = _enableMagnifier;

        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public RegionCaptureControl(MonitorInfo monitor) : this(monitor, null)
    {
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
        if (!_enableWindowSnapping)
        {
            _hoveredWindow = null;
            return;
        }

        _hoveredWindow = _windowService.GetWindowAtPoint(_currentPoint);
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
        Rect? clearRect = null;

        // Determine the clear rect (selection or window snap area)
        if (_state == CaptureState.Dragging || _state == CaptureState.Selected)
        {
            if (!_selectionRect.IsEmpty)
            {
                clearRect = PhysicalRectToLocal(_selectionRect);
            }
        }
        else if (_state == CaptureState.Hovering && _hoveredWindow is not null)
        {
            clearRect = PhysicalRectToLocal(_hoveredWindow.SnapBounds);
        }

        // Draw dimmed background with cutout using geometry clipping
        if (clearRect.HasValue && !clearRect.Value.IsEmpty())
        {
            // Create combined geometry for XOR-style rendering
            var outerGeometry = new RectangleGeometry(bounds);
            var innerGeometry = new RectangleGeometry(clearRect.Value);
            var combinedGeometry = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                outerGeometry,
                innerGeometry);

            context.DrawGeometry(DimBrush, null, combinedGeometry);

            // Draw the selection/snap border with shadow effect
            if (_state == CaptureState.Dragging || _state == CaptureState.Selected)
            {
                // Shadow first, then border
                context.DrawRectangle(null, SelectionShadowPen, clearRect.Value);
                context.DrawRectangle(null, SelectionPen, clearRect.Value);

                // Draw resize handles at corners
                DrawResizeHandles(context, clearRect.Value);

                // Draw dimensions text
                DrawDimensionsText(context, clearRect.Value);
            }
            else if (_hoveredWindow is not null)
            {
                // Window snap highlight
                context.DrawRectangle(null, WindowSnapShadowPen, clearRect.Value);
                context.DrawRectangle(null, WindowSnapPen, clearRect.Value);

                // Draw window title
                DrawWindowTitle(context, clearRect.Value, _hoveredWindow.Title);
            }
        }
        else
        {
            // No selection, just draw full dim overlay
            context.DrawRectangle(DimBrush, null, bounds);
        }

        // Draw crosshair at cursor position
        DrawCrosshair(context, bounds);

        // Draw magnifier near cursor
        if (_enableMagnifier)
        {
            DrawMagnifierPosition(context);
        }
    }

    private void DrawResizeHandles(DrawingContext context, Rect rect)
    {
        const double handleSize = 8;
        var handleBrush = Brushes.White;
        var handlePen = new Pen(Brushes.Black, 1);

        var corners = new[]
        {
            new Point(rect.Left, rect.Top),
            new Point(rect.Right, rect.Top),
            new Point(rect.Left, rect.Bottom),
            new Point(rect.Right, rect.Bottom)
        };

        foreach (var corner in corners)
        {
            var handleRect = new Rect(
                corner.X - handleSize / 2,
                corner.Y - handleSize / 2,
                handleSize,
                handleSize);

            context.DrawRectangle(handleBrush, handlePen, handleRect);
        }
    }

    private void DrawCrosshair(DrawingContext context, Rect bounds)
    {
        var cursorLocal = PhysicalToLocal(_currentPoint);

        // Only draw if cursor is within bounds
        if (!bounds.Contains(cursorLocal))
            return;

        var crosshairPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1);

        // Vertical line
        context.DrawLine(crosshairPen,
            new Point(cursorLocal.X, 0),
            new Point(cursorLocal.X, bounds.Height));

        // Horizontal line
        context.DrawLine(crosshairPen,
            new Point(0, cursorLocal.Y),
            new Point(bounds.Width, cursorLocal.Y));
    }

    private void DrawMagnifierPosition(DrawingContext context)
    {
        // Position magnifier near cursor but offset to not obstruct view
        var cursorLocal = PhysicalToLocal(_currentPoint);
        const double magnifierOffset = 20;
        const double magnifierSize = 120;

        var x = cursorLocal.X + magnifierOffset;
        var y = cursorLocal.Y + magnifierOffset;

        // Keep magnifier on screen
        if (x + magnifierSize > Bounds.Width)
            x = cursorLocal.X - magnifierOffset - magnifierSize;
        if (y + magnifierSize + 25 > Bounds.Height)
            y = cursorLocal.Y - magnifierOffset - magnifierSize - 25;

        // Update magnifier position
        _magnifier.UpdatePosition(_currentPoint);

        // For now, draw a placeholder - actual pixel capture would require screen capture
        DrawMagnifierPlaceholder(context, new Rect(x, y, magnifierSize, magnifierSize + 25));
    }

    private void DrawMagnifierPlaceholder(DrawingContext context, Rect rect)
    {
        // Draw magnifier background
        context.DrawRectangle(InfoBackgroundBrush, new Pen(Brushes.White, 2), rect, 4, 4);

        // Draw crosshair pattern in center
        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + (rect.Height - 25) / 2;

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 0.5);
        var highlightPen = new Pen(Brushes.Red, 1.5);

        // Grid lines
        for (int i = 0; i < 15; i++)
        {
            var offset = (i - 7) * 7;
            context.DrawLine(gridPen,
                new Point(rect.X + 4, centerY + offset),
                new Point(rect.X + rect.Width - 4, centerY + offset));
            context.DrawLine(gridPen,
                new Point(centerX + offset, rect.Y + 4),
                new Point(centerX + offset, rect.Y + rect.Height - 29));
        }

        // Center highlight
        context.DrawRectangle(null, highlightPen, new Rect(centerX - 3.5, centerY - 3.5, 7, 7));

        // Draw coordinates text
        var coordText = $"({_currentPoint.X:F0}, {_currentPoint.Y:F0})";
        var formattedCoord = new FormattedText(
            coordText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas", FontStyle.Normal, FontWeight.Normal),
            10,
            Brushes.White);

        context.DrawText(formattedCoord, new Point(rect.X + 4, rect.Bottom - 20));
    }

    private void DrawDimensionsText(DrawingContext context, Rect rect)
    {
        var text = $"{_selectionRect.Width:F0} x {_selectionRect.Height:F0}";

        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold),
            14,
            Brushes.White);

        var textX = rect.X + (rect.Width - formattedText.Width) / 2;
        var textY = rect.Bottom + 8;

        // Ensure text stays on screen
        if (textY + formattedText.Height > Bounds.Height - 10)
            textY = rect.Top - formattedText.Height - 8;

        // Clamp to horizontal bounds
        textX = Math.Max(8, Math.Min(Bounds.Width - formattedText.Width - 8, textX));

        // Draw text background with rounded corners
        var textBounds = new Rect(textX - 8, textY - 4,
            formattedText.Width + 16, formattedText.Height + 8);
        context.DrawRectangle(InfoBackgroundBrush, null, textBounds, 4, 4);

        // Draw text
        context.DrawText(formattedText, new Point(textX, textY));
    }

    private void DrawWindowTitle(DrawingContext context, Rect rect, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;

        // Truncate long titles
        if (title.Length > 50)
            title = string.Concat(title.AsSpan(0, 47), "...");

        var formattedText = new FormattedText(
            title,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI", FontStyle.Normal, FontWeight.Normal),
            12,
            Brushes.White);

        var textX = rect.X + (rect.Width - formattedText.Width) / 2;
        var textY = rect.Top - formattedText.Height - 8;

        // Ensure text stays on screen
        if (textY < 10)
            textY = rect.Bottom + 8;

        // Clamp to horizontal bounds
        textX = Math.Max(8, Math.Min(Bounds.Width - formattedText.Width - 8, textX));

        // Draw text background
        var textBounds = new Rect(textX - 8, textY - 4,
            formattedText.Width + 16, formattedText.Height + 8);
        context.DrawRectangle(InfoBackgroundBrush, null, textBounds, 4, 4);

        // Draw text
        context.DrawText(formattedText, new Point(textX, textY));
    }
}
