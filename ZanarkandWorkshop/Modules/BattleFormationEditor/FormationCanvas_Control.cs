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
using FFXProjectEditor.FfxLib.Battlefield;

namespace FFXProjectEditor.Modules.BattleFormationEditor;

public sealed class FormationCanvas_Control : Control
{
    // The normal editing camera matches the former 400% view. This keeps the
    // position markers comfortably separated while lower percentages remain
    // available for viewing the complete battlefield footprint.
    private const double DefaultCameraMultiplier = 5.6;
    public static readonly StyledProperty<IEnumerable<FormationPositionRow>?> ItemsProperty =
        AvaloniaProperty.Register<FormationCanvas_Control, IEnumerable<FormationPositionRow>?>(
            nameof(Items));

    public static readonly StyledProperty<FormationPositionRow?> SelectedItemProperty =
        AvaloniaProperty.Register<FormationCanvas_Control, FormationPositionRow?>(
            nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<BattlefieldHeightMap?> BattlefieldProperty =
        AvaloniaProperty.Register<FormationCanvas_Control, BattlefieldHeightMap?>(
            nameof(Battlefield));

    public static readonly StyledProperty<object?> CameraKeyProperty =
        AvaloniaProperty.Register<FormationCanvas_Control, object?>(nameof(CameraKey));

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

    public BattlefieldHeightMap? Battlefield
    {
        get => GetValue(BattlefieldProperty);
        set => SetValue(BattlefieldProperty, value);
    }

    /// <summary>
    /// Identifies the content currently displayed by the canvas. A new key always
    /// starts a fresh centered camera, even when the bound collections are reused.
    /// </summary>
    public object? CameraKey
    {
        get => GetValue(CameraKeyProperty);
        set => SetValue(CameraKeyProperty, value);
    }

    public int ZoomPercent
    {
        get => _zoomPercent;
        private set => SetAndRaise(ZoomPercentProperty, ref _zoomPercent, value);
    }

    static FormationCanvas_Control()
    {
        AffectsRender<FormationCanvas_Control>(ItemsProperty, SelectedItemProperty, BattlefieldProperty);
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
        else if (change.Property == BattlefieldProperty)
        {
            ResetTransform();
            InvalidateVisual();
        }
        else if (change.Property == CameraKeyProperty)
        {
            ResetTransform();
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#15171A")), Bounds);
        DrawGrid(context);

        FormationPositionRow[] points = Items?.ToArray() ?? Array.Empty<FormationPositionRow>();
        if (!_transform.IsValid || _transformSize != Bounds.Size)
        {
            FormationPositionRow[] cameraPoints = points;
            if (Battlefield is null)
            {
                FormationPositionRow[] activeCombatPoints = points.Where(point =>
                    point.Kind is
                        FfxLib.BattleFormation.FormationPositionKind.Party or
                        FfxLib.BattleFormation.FormationPositionKind.Monster).ToArray();
                if (activeCombatPoints.Length > 0)
                    cameraPoints = activeCombatPoints;
            }
            ViewTransform fullField = ViewTransform.Create(cameraPoints, Battlefield, Bounds);
            _transformSize = Bounds.Size;
            // Battlefield previews use the closer editing camera requested for
            // normal formations. When no surface exists, frame every available
            // marker so unusual formations do not start with positions off-screen.
            double cameraMultiplier = Battlefield is null ? 1 : DefaultCameraMultiplier;
            _fitScale = fullField.Scale * cameraMultiplier;
            int initialZoomPercent = Battlefield is null ? 50 : 100;
            double initialScale = _fitScale * initialZoomPercent / 100d;
            _transform = fullField.ZoomAt(
                new Point(Bounds.Width / 2, Bounds.Height / 2), initialScale);
            ZoomPercent = initialZoomPercent;
        }
        DrawBattlefield(context);
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
        // Enemy-party edits can add or remove visible markers. Preserve the
        // user's current camera while the formation itself remains selected.
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
        _fitScale = 0;
        _dragged = null;
        _dragStarted = false;
        _panning = false;
        ZoomPercent = 100;
    }

    public void ZoomIn() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1.2);

    public void ZoomOut() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1 / 1.2);

    public void CenterOn(FormationPositionRow? position)
    {
        if (position is null || !_transform.IsValid)
            return;

        Point screen = _transform.ToScreen(position.X, position.Z);
        _transform = _transform.Translate(
            Bounds.Width / 2 - screen.X,
            Bounds.Height / 2 - screen.Y);
        InvalidateVisual();
    }

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
            _transform.Scale * factor, _fitScale * 0.15, _fitScale * 12);
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

    private void DrawBattlefield(DrawingContext context)
    {
        if (Battlefield is not { } battlefield || !_transform.IsValid)
            return;

        var fill = new SolidColorBrush(Color.Parse("#262A8391"));
        var boundaryPen = new Pen(new SolidColorBrush(Color.Parse("#C054C7D8")), 2);
        IReadOnlyList<BattlefieldVertex> vertices = battlefield.Vertices;
        var edgeCounts = new Dictionary<(ushort A, ushort B), int>();
        foreach (BattlefieldTriangle triangle in battlefield.Triangles)
        {
            Point a = _transform.ToScreen(vertices[triangle.A].X, vertices[triangle.A].Z);
            Point b = _transform.ToScreen(vertices[triangle.B].X, vertices[triangle.B].Z);
            Point c = _transform.ToScreen(vertices[triangle.C].X, vertices[triangle.C].Z);
            var geometry = new StreamGeometry();
            using (StreamGeometryContext path = geometry.Open())
            {
                path.BeginFigure(a, true);
                path.LineTo(b);
                path.LineTo(c);
                path.EndFigure(true);
            }
            context.DrawGeometry(fill, null, geometry);

            CountEdge(edgeCounts, triangle.A, triangle.B);
            CountEdge(edgeCounts, triangle.B, triangle.C);
            CountEdge(edgeCounts, triangle.C, triangle.A);
        }

        foreach (((ushort a, ushort b), int count) in edgeCounts)
        {
            if (count != 1)
                continue;
            Point start = _transform.ToScreen(vertices[a].X, vertices[a].Z);
            Point end = _transform.ToScreen(vertices[b].X, vertices[b].Z);
            context.DrawLine(boundaryPen, start, end);
        }
    }

    private static void CountEdge(
        Dictionary<(ushort A, ushort B), int> counts, ushort first, ushort second)
    {
        (ushort A, ushort B) edge = first < second ? (first, second) : (second, first);
        counts[edge] = counts.TryGetValue(edge, out int count) ? count + 1 : 1;
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

        public static ViewTransform Create(
            FormationPositionRow[] points,
            BattlefieldHeightMap? battlefield,
            Rect bounds)
        {
            BattlefieldVertex[] surface = battlefield?.Vertices.ToArray() ?? [];
            if (points.Length == 0 && surface.Length == 0)
                return new ViewTransform(
                    -1, -1, 1, 20, 20,
                    Math.Max(1, bounds.Width - 40), Math.Max(1, bounds.Height - 40));
            double minX = Math.Min(
                points.Length == 0 ? double.PositiveInfinity : points.Min(point => (double)point.X),
                surface.Length == 0 ? double.PositiveInfinity : surface.Min(point => (double)point.X));
            double maxX = Math.Max(
                points.Length == 0 ? double.NegativeInfinity : points.Max(point => (double)point.X),
                surface.Length == 0 ? double.NegativeInfinity : surface.Max(point => (double)point.X));
            double minZ = Math.Min(
                points.Length == 0 ? double.PositiveInfinity : points.Min(point => (double)point.Z),
                surface.Length == 0 ? double.PositiveInfinity : surface.Min(point => (double)point.Z));
            double maxZ = Math.Max(
                points.Length == 0 ? double.NegativeInfinity : points.Max(point => (double)point.Z),
                surface.Length == 0 ? double.NegativeInfinity : surface.Max(point => (double)point.Z));
            double rawWidth = Math.Max(1, maxX - minX);
            double rawHeight = Math.Max(1, maxZ - minZ);

            // Keep a small proportional editing margin. Screen-space padding below
            // provides the main breathing room, so compact battlefields still use
            // most of the viewer at the default 100% camera.
            double editingMargin = Math.Max(rawWidth, rawHeight) * 0.05;
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
