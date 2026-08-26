using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using FFXProjectEditor.FfxLib.TreasureMap;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

public sealed class TreasureMapCanvas_Control : Control
{
    private static readonly IBrush MapBackground = new SolidColorBrush(Color.Parse("#101820"));
    private static readonly IBrush MapFill = new SolidColorBrush(Color.Parse("#183F4A"));
    private static readonly Pen MapEdge = new(new SolidColorBrush(Color.Parse("#4CB7C5")), 1);
    public static readonly StyledProperty<GuideMapModel?> ModelProperty = AvaloniaProperty.Register<TreasureMapCanvas_Control, GuideMapModel?>(nameof(Model));
    public static readonly StyledProperty<IEnumerable<TreasureChestRow>?> ItemsProperty = AvaloniaProperty.Register<TreasureMapCanvas_Control, IEnumerable<TreasureChestRow>?>(nameof(Items));
    public static readonly StyledProperty<TreasureChestRow?> SelectedItemProperty = AvaloniaProperty.Register<TreasureMapCanvas_Control, TreasureChestRow?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);
    public static readonly DirectProperty<TreasureMapCanvas_Control, int> ZoomPercentProperty = AvaloniaProperty.RegisterDirect<TreasureMapCanvas_Control, int>(nameof(ZoomPercent), control => control.ZoomPercent);
    private double _zoom = 1; private Vector _pan; private bool _panning; private Point _last; private int _zoomPercent = 100;
    private GuideMapModel? _renderModel;
    private StreamGeometry? _renderGeometry;
    private GuideMapProjection? _renderProjection;
    private double _renderWidth;
    private double _renderHeight;
    public GuideMapModel? Model { get => GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public IEnumerable<TreasureChestRow>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public TreasureChestRow? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public int ZoomPercent { get => _zoomPercent; private set => SetAndRaise(ZoomPercentProperty, ref _zoomPercent, value); }
    static TreasureMapCanvas_Control() => AffectsRender<TreasureMapCanvas_Control>(ModelProperty, ItemsProperty, SelectedItemProperty);
    public TreasureMapCanvas_Control()
    {
        ClipToBounds = true; PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released; PointerWheelChanged += Wheeled;
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ModelProperty || change.Property == BoundsProperty)
        {
            ClearRenderCache();
            if (change.Property == ModelProperty) Fit();
        }
    }
    public void Fit() { _zoom = 1; _pan = default; ZoomPercent = 100; InvalidateVisual(); }
    public void ZoomIn() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1.2);
    public void ZoomOut() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1 / 1.2);
    public void CenterOn(TreasureChestRow? chest)
    {
        if (Model is null || chest?.Location.GuideX is not float x ||
            chest.Location.GuideZ is not float z)
            return;

        EnsureRenderCache();
        (float projectedX, float projectedY) = _renderProjection!.Project(x, z);
        _pan = new Vector(
            -(projectedX - Bounds.Width / 2) * _zoom,
            -(projectedY - Bounds.Height / 2) * _zoom);
        InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(MapBackground, Bounds);
        if (Model is null) return;
        EnsureRenderCache();
        GuideMapProjection projection = _renderProjection!;
        Point Screen(float x, float z) { var p = projection.Project(x, z); return new Point((p.X - Bounds.Width / 2) * _zoom + Bounds.Width / 2 + _pan.X, (p.Y - Bounds.Height / 2) * _zoom + Bounds.Height / 2 + _pan.Y); }
        using (context.PushTransform(Matrix.CreateTranslation(-Bounds.Width / 2, -Bounds.Height / 2)
            * Matrix.CreateScale(_zoom, _zoom)
            * Matrix.CreateTranslation(Bounds.Width / 2 + _pan.X, Bounds.Height / 2 + _pan.Y)))
            context.DrawGeometry(MapFill, MapEdge, _renderGeometry);
        foreach (TreasureChestRow chest in Items ?? [])
        {
            if (chest.Location.GuideX is not float x || chest.Location.GuideZ is not float z) continue;
            Point p = Screen(x, z); bool selected = ReferenceEquals(chest, SelectedItem);
            context.DrawEllipse(Brushes.Gold, new Pen(selected ? Brushes.White : Brushes.DarkGoldenrod, selected ? 3 : 1), p, selected ? 9 : 7, selected ? 9 : 7);
        }
    }
    private TreasureChestRow? Hit(Point pointer)
    {
        if (Model is null) return null;
        EnsureRenderCache();
        GuideMapProjection projection = _renderProjection!;
        return (Items ?? []).Where(c => c.Location.GuideX.HasValue && c.Location.GuideZ.HasValue).Select(c => { var p = projection.Project(c.Location.GuideX!.Value, c.Location.GuideZ!.Value); var screen = new Point((p.X-Bounds.Width/2)*_zoom+Bounds.Width/2+_pan.X,(p.Y-Bounds.Height/2)*_zoom+Bounds.Height/2+_pan.Y); double dx=screen.X-pointer.X, dy=screen.Y-pointer.Y; return (c,d:Math.Sqrt(dx*dx+dy*dy)); }).Where(x => x.d <= 14).OrderBy(x => x.d).Select(x => x.c).FirstOrDefault();
    }
    private void Pressed(object? s, PointerPressedEventArgs e) { Point p=e.GetPosition(this); TreasureChestRow? hit=Hit(p); if(hit is not null){SelectedItem=hit;InvalidateVisual();}else{_panning=true;_last=p;e.Pointer.Capture(this);} }
    private void Moved(object? s, PointerEventArgs e) { Point p=e.GetPosition(this); if(_panning&&e.GetCurrentPoint(this).Properties.IsLeftButtonPressed){_pan+=p-_last;ClampToVisibleMap();_last=p;InvalidateVisual();return;} TreasureChestRow? hit=Hit(p); ToolTip.SetTip(this, hit is null ? null : $"{hit.Label}\n{hit.PositionText}\n{hit.Confidence}"); }
    private void Released(object? s, PointerReleasedEventArgs e){_panning=false;e.Pointer.Capture(null);}
    private void Wheeled(object? s, PointerWheelEventArgs e){ZoomAt(e.GetPosition(this),e.Delta.Y>0?1.15:1/1.15);e.Handled=true;}
    private void ZoomAt(Point anchor,double factor){double next=Math.Clamp(_zoom*factor,.6,6);double ratio=next/_zoom;_pan=new Vector(anchor.X-Bounds.Width/2-(anchor.X-Bounds.Width/2-_pan.X)*ratio,anchor.Y-Bounds.Height/2-(anchor.Y-Bounds.Height/2-_pan.Y)*ratio);_zoom=next;ClampToVisibleMap();ZoomPercent=(int)Math.Round(_zoom*100);InvalidateVisual();}

    private void ClampToVisibleMap()
    {
        if (Model is null || Model.Vertices.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        EnsureRenderCache();
        GuideMapProjection projection = _renderProjection!;
        double minimumX = double.MaxValue;
        double maximumX = double.MinValue;
        double minimumY = double.MaxValue;
        double maximumY = double.MinValue;
        foreach (GuideMapVertex vertex in Model.Vertices)
        {
            (float projectedX, float projectedY) = projection.Project(vertex.X, vertex.Z);
            double screenX = (projectedX - Bounds.Width / 2) * _zoom + Bounds.Width / 2 + _pan.X;
            double screenY = (projectedY - Bounds.Height / 2) * _zoom + Bounds.Height / 2 + _pan.Y;
            minimumX = Math.Min(minimumX, screenX);
            maximumX = Math.Max(maximumX, screenX);
            minimumY = Math.Min(minimumY, screenY);
            maximumY = Math.Max(maximumY, screenY);
        }

        double centerX = Bounds.Width / 2;
        double centerY = Bounds.Height / 2;
        double adjustmentX = maximumX < centerX
            ? centerX - maximumX
            : minimumX > centerX ? centerX - minimumX : 0;
        double adjustmentY = maximumY < centerY
            ? centerY - maximumY
            : minimumY > centerY ? centerY - minimumY : 0;
        _pan += new Vector(adjustmentX, adjustmentY);
    }

    private void EnsureRenderCache()
    {
        if (Model is null) return;
        if (ReferenceEquals(_renderModel, Model) && _renderGeometry is not null &&
            _renderWidth == Bounds.Width && _renderHeight == Bounds.Height) return;

        _renderModel = Model;
        _renderWidth = Bounds.Width;
        _renderHeight = Bounds.Height;
        _renderProjection = GuideMapProjection.Fit(Model, Math.Max(100, (int)Bounds.Width), Math.Max(100, (int)Bounds.Height));
        var combined = new StreamGeometry();
        using (StreamGeometryContext geometry = combined.Open())
        {
            foreach (GuideMapTriangle triangle in Model.Triangles)
            {
                GuideMapVertex a = Model.Vertices[triangle.A], b = Model.Vertices[triangle.B], c = Model.Vertices[triangle.C];
                (float ax, float ay) = _renderProjection.Project(a.X, a.Z);
                (float bx, float by) = _renderProjection.Project(b.X, b.Z);
                (float cx, float cy) = _renderProjection.Project(c.X, c.Z);
                geometry.BeginFigure(new Point(ax, ay), true);
                geometry.LineTo(new Point(bx, by));
                geometry.LineTo(new Point(cx, cy));
                geometry.EndFigure(true);
            }
        }
        _renderGeometry = combined;
    }

    private void ClearRenderCache()
    {
        _renderModel = null;
        _renderGeometry = null;
        _renderProjection = null;
        _renderWidth = 0;
        _renderHeight = 0;
    }
}
