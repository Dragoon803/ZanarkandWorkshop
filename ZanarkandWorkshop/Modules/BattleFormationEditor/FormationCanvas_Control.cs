using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace FFXProjectEditor.Modules.BattleFormationEditor;

public sealed class FormationCanvas_Control : Control
{
    public static readonly StyledProperty<IEnumerable<FormationPositionRow>?> ItemsProperty =
        AvaloniaProperty.Register<FormationCanvas_Control, IEnumerable<FormationPositionRow>?>(
            nameof(Items));

    public static readonly StyledProperty<FormationPositionRow?> SelectedItemProperty =
        AvaloniaProperty.Register<FormationCanvas_Control, FormationPositionRow?>(
            nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<FormationCanvas_Control, int> ZoomPercentProperty =
        AvaloniaProperty.RegisterDirect<FormationCanvas_Control, int>(
            nameof(ZoomPercent), control => control.ZoomPercent);

    private INotifyCollectionChanged? _collection;
    private FormationPositionRow? _dragged;
    private Point _dragStartPoint;
    private Vector _dragOffset;
    private bool _dragStarted;
    private bool _panning;
    private Point _lastPanPoint;
    private ViewTransform _transform;
    private Size _transformSize;
    private double _fitScale;
    private int _zoomPercent = 100;

    public IEnumerable<FormationPositionRow>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public FormationPositionRow? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public int ZoomPercent
    {
        get => _zoomPercent;
        private set => SetAndRaise(ZoomPercentProperty, ref _zoomPercent, value);
    }

    static FormationCanvas_Control()
    {
        AffectsRender<FormationCanvas_Control>(ItemsProperty, SelectedItemProperty);
    }

    public FormationCanvas_Control()
    {
        ClipToBounds = true;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerExited += OnPointerExited;
        PointerWheelChanged += OnPointerWheelChanged;
        ToolTip.SetShowDelay(this, 150);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
            ItemsChanged(change.NewValue as IEnumerable<FormationPositionRow>);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#15171A")), Bounds);
        DrawGrid(context);

        FormationPositionRow[] points = Items?.ToArray() ?? Array.Empty<FormationPositionRow>();
        if (!_transform.IsValid || _transformSize != Bounds.Size)
        {
            _transform = ViewTransform.Create(points, Bounds);
            _transformSize = Bounds.Size;
            _fitScale = _transform.Scale;
            ZoomPercent = 100;
        }
        foreach (FormationPositionRow point in points)
        {
            Point screen = _transform.ToScreen(point.X, point.Z);
            bool selected = ReferenceEquals(point, SelectedItem);
            double radius = selected ? 11 : 9;
            IBrush brush = BrushFor(point.Kind);
            context.DrawEllipse(brush,
                selected ? new Pen(Brushes.White, 2) : null,
                screen, radius, radius);

            var number = new FormattedText(
                point.Marker,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial", FontStyle.Normal, FontWeight.Bold),
                11,
                TextBrushFor(point.Kind));
            context.DrawText(number,
                new Point(screen.X - number.Width / 2, screen.Y - number.Height / 2));
        }
    }

    private void ItemsChanged(IEnumerable<FormationPositionRow>? items)
    {
        if (_collection is not null)
            _collection.CollectionChanged -= CollectionChanged;
        UnsubscribeRows();

        _collection = items as INotifyCollectionChanged;
        if (_collection is not null)
            _collection.CollectionChanged += CollectionChanged;
        SubscribeRows();
        ResetTransform();
        InvalidateVisual();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnsubscribeRows();
        SubscribeRows();
        ResetTransform();
        InvalidateVisual();
    }

    private void SubscribeRows()
    {
        if (Items is null) return;
        foreach (FormationPositionRow row in Items)
            row.PropertyChanged += RowPropertyChanged;
    }

    private void UnsubscribeRows()
    {
        if (Items is null) return;
        foreach (FormationPositionRow row in Items)
            row.PropertyChanged -= RowPropertyChanged;
    }

    private void RowPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        InvalidateVisual();

    private void ResetTransform()
    {
        _transform = default;
        _transformSize = default;
    }

    public void ZoomIn() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1.2);

    public void ZoomOut() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1 / 1.2);

    public void Fit()
    {
        ResetTransform();
        ZoomPercent = 100;
        InvalidateVisual();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    private void ZoomAt(Point anchor, double factor)
    {
        if (!_transform.IsValid || _fitScale <= 0)
            return;

        double targetScale = Math.Clamp(
            _transform.Scale * factor, _fitScale * 0.25, _fitScale * 4);
        _transform = ClampToVisiblePoints(
            _transform.ZoomAt(anchor, targetScale).ClampToBounds(Bounds));
        ZoomPercent = (int)Math.Round(targetScale / _fitScale * 100);
        InvalidateVisual();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        FormationPositionRow[] points = Items?.ToArray() ?? Array.Empty<FormationPositionRow>();
        Point pointer = e.GetPosition(this);
        FormationPositionRow? nearest = points
            .Select(point => (Point: point, Screen: _transform.ToScreen(point.X, point.Z)))
            .Where(candidate => Distance(candidate.Screen, pointer) <= 14)
            .OrderBy(candidate => Distance(candidate.Screen, pointer))
            .Select(candidate => candidate.Point)
            .FirstOrDefault();
        if (nearest is null)
        {
            _panning = true;
            _lastPanPoint = pointer;
            e.Pointer.Capture(this);
            return;
        }

        SelectedItem = nearest;
        _dragged = nearest;
        _dragStartPoint = pointer;
        _dragOffset = _transform.ToScreen(nearest.X, nearest.Z) - pointer;
        _dragStarted = false;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        Point pointer = e.GetPosition(this);
        if (_panning && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Vector movement = pointer - _lastPanPoint;
            _transform = ClampToVisiblePoints(
                _transform.PanBy(movement.X, movement.Y, Bounds));
            _lastPanPoint = pointer;
            ToolTip.SetTip(this, null);
            InvalidateVisual();
            return;
        }

        if (_dragged is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            FormationPositionRow? hovered = FindNearest(pointer, 14);
            ToolTip.SetTip(this, hovered?.TooltipText);
            return;
        }
        ToolTip.SetTip(this, null);
        if (!_dragStarted)
        {
            if (Distance(pointer, _dragStartPoint) < 4)
                return;
            _dragStarted = true;
        }
        Point target = pointer + _dragOffset;
        var boundedPointer = new Point(
            Math.Clamp(target.X, 0, Bounds.Width),
            Math.Clamp(target.Y, 0, Bounds.Height));
        (float x, float z) = _transform.ToWorld(boundedPointer);
        _dragged.X = x;
        _dragged.Z = z;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragged = null;
        _dragStarted = false;
        _panning = false;
        e.Pointer.Capture(null);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e) =>
        ToolTip.SetTip(this, null);

    private FormationPositionRow? FindNearest(Point pointer, double maximumDistance) =>
        (Items?.ToArray() ?? Array.Empty<FormationPositionRow>())
        .Select(point => (Point: point, Screen: _transform.ToScreen(point.X, point.Z)))
        .Where(candidate => Distance(candidate.Screen, pointer) <= maximumDistance)
        .OrderBy(candidate => Distance(candidate.Screen, pointer))
        .Select(candidate => candidate.Point)
        .FirstOrDefault();

    private ViewTransform ClampToVisiblePoints(ViewTransform transform)
    {
        FormationPositionRow[] points = Items?.ToArray() ?? Array.Empty<FormationPositionRow>();
        if (points.Length == 0)
            return transform;

        Point[] screenPoints = points
            .Select(point => transform.ToScreen(point.X, point.Z))
            .ToArray();
        double minX = screenPoints.Min(point => point.X);
        double maxX = screenPoints.Max(point => point.X);
        double minY = screenPoints.Min(point => point.Y);
        double maxY = screenPoints.Max(point => point.Y);
        double visibleX = Math.Min(50, Bounds.Width / 2);
        double visibleY = Math.Min(50, Bounds.Height / 2);
        double adjustmentX = maxX < visibleX
            ? visibleX - maxX
            : minX > Bounds.Width - visibleX
                ? Bounds.Width - visibleX - minX
                : 0;
        double adjustmentY = maxY < visibleY
            ? visibleY - maxY
            : minY > Bounds.Height - visibleY
                ? Bounds.Height - visibleY - minY
                : 0;
        return transform.Translate(adjustmentX, adjustmentY);
    }

    private void DrawGrid(DrawingContext context)
    {
        var pen = new Pen(new SolidColorBrush(Color.Parse("#292D32")), 1);
        const double spacing = 40;
        for (double x = 0; x < Bounds.Width; x += spacing)
            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        for (double y = 0; y < Bounds.Height; y += spacing)
            context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
    }

    private static IBrush BrushFor(FfxLib.BattleFormation.FormationPositionKind kind) => kind switch
    {
        FfxLib.BattleFormation.FormationPositionKind.Party => Brushes.DodgerBlue,
        FfxLib.BattleFormation.FormationPositionKind.PartySecondary => Brushes.Cyan,
        FfxLib.BattleFormation.FormationPositionKind.Aeon => Brushes.Orange,
        FfxLib.BattleFormation.FormationPositionKind.Monster => Brushes.Crimson,
        _ => Brushes.IndianRed
    };

    private static IBrush TextBrushFor(FfxLib.BattleFormation.FormationPositionKind kind) => kind switch
    {
        FfxLib.BattleFormation.FormationPositionKind.PartySecondary => Brushes.Black,
        FfxLib.BattleFormation.FormationPositionKind.Aeon => Brushes.Black,
        _ => Brushes.White
    };

    private static double Distance(Point a, Point b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private readonly record struct ViewTransform(
        double MinX, double MinZ, double Scale, double Left, double Top,
        double Width, double Height)
    {
        public bool IsValid => Scale > 0;

        public static ViewTransform Create(FormationPositionRow[] points, Rect bounds)
        {
            if (points.Length == 0)
                return new ViewTransform(
                    -1, -1, 1, 20, 20,
                    Math.Max(1, bounds.Width - 40), Math.Max(1, bounds.Height - 40));
            double minX = points.Min(point => (double)point.X);
            double maxX = points.Max(point => (double)point.X);
            double minZ = points.Min(point => (double)point.Z);
            double maxZ = points.Max(point => (double)point.Z);
            double rawWidth = Math.Max(1, maxX - minX);
            double rawHeight = Math.Max(1, maxZ - minZ);

            // Reserve the same amount of world-coordinate editing room in every
            // direction instead of fitting the outermost points to the canvas.
            double editingMargin = Math.Max(rawWidth, rawHeight) * 0.25;
            minX -= editingMargin;
            maxX += editingMargin;
            minZ -= editingMargin;
            maxZ += editingMargin;

            double width = maxX - minX;
            double height = maxZ - minZ;
            double availableWidth = Math.Max(1, bounds.Width - 50);
            double availableHeight = Math.Max(1, bounds.Height - 50);
            double scale = Math.Min(availableWidth / width, availableHeight / height);
            double contentWidth = width * scale;
            double contentHeight = height * scale;
            double left = (bounds.Width - contentWidth) / 2;
            double top = (bounds.Height - contentHeight) / 2;
            return new ViewTransform(
                minX, minZ, scale, left, top, contentWidth, contentHeight);
        }

        public Point ToScreen(float x, float z) =>
            new(Left + (x - MinX) * Scale, Top + Height - (z - MinZ) * Scale);

        public (float X, float Z) ToWorld(Point point) =>
            ((float)(MinX + (point.X - Left) / Scale),
             (float)(MinZ + (Top + Height - point.Y) / Scale));

        public ViewTransform ZoomAt(Point anchor, double newScale)
        {
            (float anchorX, float anchorZ) = ToWorld(anchor);
            double worldWidth = Width / Scale;
            double worldHeight = Height / Scale;
            double newWidth = worldWidth * newScale;
            double newHeight = worldHeight * newScale;
            double newLeft = anchor.X - (anchorX - MinX) * newScale;
            double newTop = anchor.Y - newHeight + (anchorZ - MinZ) * newScale;
            return new ViewTransform(
                MinX, MinZ, newScale, newLeft, newTop, newWidth, newHeight);
        }

        public ViewTransform PanBy(double x, double y, Rect bounds) =>
            (this with { Left = Left + x, Top = Top + y }).ClampToBounds(bounds);

        public ViewTransform Translate(double x, double y) =>
            this with { Left = Left + x, Top = Top + y };

        public ViewTransform ClampToBounds(Rect bounds)
        {
            double visibleX = Math.Min(50, bounds.Width / 2);
            double visibleY = Math.Min(50, bounds.Height / 2);
            double left = Math.Clamp(Left, visibleX - Width, bounds.Width - visibleX);
            double top = Math.Clamp(Top, visibleY - Height, bounds.Height - visibleY);
            return this with { Left = left, Top = top };
        }
    }
}
