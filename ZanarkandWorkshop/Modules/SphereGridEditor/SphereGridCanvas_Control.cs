using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using FFXProjectEditor.FfxLib.SphereGrid;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FFXProjectEditor.Modules.SphereGridEditor;

public sealed class SphereGridCanvas_Control : Control
{
    public event EventHandler<SphereGridNode>? NodeSelectionRequested;
    public event EventHandler<int>? NodeDragStarted;
    public event EventHandler<NodePositionPreviewEventArgs>? NodePositionPreviewRequested;
    public event EventHandler<int>? NodeDragCompleted;
    public event EventHandler<int>? LinkSelectionRequested;
    public event EventHandler? EmptySpaceSelectionRequested;

    public static readonly StyledProperty<SphereGridGraph?> GraphProperty =
        AvaloniaProperty.Register<SphereGridCanvas_Control, SphereGridGraph?>(nameof(Graph));

    public static readonly StyledProperty<SphereGridNode?> SelectedNodeProperty =
        AvaloniaProperty.Register<SphereGridCanvas_Control, SphereGridNode?>(
            nameof(SelectedNode), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyDictionary<int, SphereGridCharacter>?>
        ColorOverridesProperty =
            AvaloniaProperty.Register<SphereGridCanvas_Control,
                IReadOnlyDictionary<int, SphereGridCharacter>?>(
                nameof(ColorOverrides));

    public static readonly StyledProperty<int> SelectedLinkIndexProperty =
        AvaloniaProperty.Register<SphereGridCanvas_Control, int>(
            nameof(SelectedLinkIndex), -1);

    public static readonly StyledProperty<int> PreviewAnchorNodeIndexProperty =
        AvaloniaProperty.Register<SphereGridCanvas_Control, int>(
            nameof(PreviewAnchorNodeIndex), -1);

    public static readonly StyledProperty<int> PreviewLinkNodeAIndexProperty =
        AvaloniaProperty.Register<SphereGridCanvas_Control, int>(
            nameof(PreviewLinkNodeAIndex), -1);

    public static readonly StyledProperty<int> PreviewLinkNodeBIndexProperty =
        AvaloniaProperty.Register<SphereGridCanvas_Control, int>(
            nameof(PreviewLinkNodeBIndex), -1);

    public static readonly StyledProperty<bool> IsLinkCreationModeProperty =
        AvaloniaProperty.Register<SphereGridCanvas_Control, bool>(
            nameof(IsLinkCreationMode));

    public static readonly DirectProperty<SphereGridCanvas_Control, int> ZoomPercentProperty =
        AvaloniaProperty.RegisterDirect<SphereGridCanvas_Control, int>(
            nameof(ZoomPercent), control => control.ZoomPercent);

    private GraphTransform _transform;
    private Size _transformSize;
    private double _fitScale;
    private int _zoomPercent = 100;
    private bool _panning;
    private Point _lastPanPoint;
    private int _dragNodeIndex = -1;
    private Point _dragStartPoint;
    private Vector _dragOffset;
    private bool _nodeDragStarted;
    private SphereGridGraph? _renderedGraph;

    public SphereGridGraph? Graph
    {
        get => GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public SphereGridNode? SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    public IReadOnlyDictionary<int, SphereGridCharacter>? ColorOverrides
    {
        get => GetValue(ColorOverridesProperty);
        set => SetValue(ColorOverridesProperty, value);
    }

    public int SelectedLinkIndex
    {
        get => GetValue(SelectedLinkIndexProperty);
        set => SetValue(SelectedLinkIndexProperty, value);
    }

    public int PreviewAnchorNodeIndex
    {
        get => GetValue(PreviewAnchorNodeIndexProperty);
        set => SetValue(PreviewAnchorNodeIndexProperty, value);
    }

    public int PreviewLinkNodeAIndex
    {
        get => GetValue(PreviewLinkNodeAIndexProperty);
        set => SetValue(PreviewLinkNodeAIndexProperty, value);
    }

    public int PreviewLinkNodeBIndex
    {
        get => GetValue(PreviewLinkNodeBIndexProperty);
        set => SetValue(PreviewLinkNodeBIndexProperty, value);
    }

    public bool IsLinkCreationMode
    {
        get => GetValue(IsLinkCreationModeProperty);
        set => SetValue(IsLinkCreationModeProperty, value);
    }

    public int ZoomPercent
    {
        get => _zoomPercent;
        private set => SetAndRaise(ZoomPercentProperty, ref _zoomPercent, value);
    }

    static SphereGridCanvas_Control()
    {
        AffectsRender<SphereGridCanvas_Control>(
            GraphProperty, SelectedNodeProperty, ColorOverridesProperty,
            SelectedLinkIndexProperty, PreviewAnchorNodeIndexProperty,
            PreviewLinkNodeAIndexProperty, PreviewLinkNodeBIndexProperty);
    }

    public SphereGridCanvas_Control()
    {
        ClipToBounds = true;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerExited += (_, _) => ToolTip.SetTip(this, null);
        PointerWheelChanged += OnPointerWheelChanged;
        SizeChanged += (_, _) => ResetTransformForViewportChange();
        ToolTip.SetShowDelay(this, 120);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GraphProperty)
        {
            SphereGridGraph? previous = _renderedGraph;
            _renderedGraph = Graph;
            if (previous is null ||
                Graph is null ||
                previous.File.Kind != Graph.File.Kind)
                Fit();
            else
                InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#111419")), Bounds);
        if (Graph is null)
            return;
        EnsureTransform();
        DrawClusters(context);
        DrawLinks(context);
        DrawLinkCreationPreview(context);
        DrawNodes(context);
    }

    public void ZoomIn() => ZoomAt(Center, 1.2);
    public void ZoomOut() => ZoomAt(Center, 1 / 1.2);

    public void CenterOn(SphereGridNode node)
    {
        EnsureTransform();
        if (!_transform.IsValid)
            return;
        Point screen = _transform.ToScreen(node.X, node.Y);
        _transform = _transform.Pan(Center.X - screen.X, Center.Y - screen.Y);
        InvalidateVisual();
    }

    public void Fit()
    {
        ResetTransformForViewportChange();
    }

    private void ResetTransformForViewportChange()
    {
        _transform = default;
        _transformSize = default;
        _fitScale = 0;
        ZoomPercent = 100;
        InvalidateVisual();
    }

    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);

    private void EnsureTransform()
    {
        if (Graph is null)
            return;
        if (!_transform.IsValid || _transformSize != Bounds.Size)
        {
            _transform = GraphTransform.Fit(Graph.Bounds, Bounds);
            _transformSize = Bounds.Size;
            _fitScale = _transform.Scale;
            ZoomPercent = 100;
        }
    }

    private void DrawClusters(DrawingContext context)
    {
        if (Graph is null) return;
        var fill = new SolidColorBrush(Color.Parse("#101E2733"));
        var outline = new Pen(new SolidColorBrush(Color.Parse("#304E6175")), 1);
        double[] worldRadii = { 22, 42, 64, 88 };
        foreach (SphereGridCluster cluster in Graph.File.Clusters)
        {
            Point center = _transform.ToScreen(cluster.X, cluster.Y);
            double radius = worldRadii[cluster.SizeClass] * _transform.Scale;
            context.DrawEllipse(fill, outline, center, radius, radius);
        }
    }

    private void DrawLinks(DrawingContext context)
    {
        if (Graph is null) return;
        int selectedIndex = SelectedNode?.Index ?? -1;
        foreach (SphereGridLink link in Graph.File.Links)
        {
            bool selectedLink = link.Index == SelectedLinkIndex;
            bool highlighted = selectedLink || selectedIndex >= 0 &&
                Graph.IsLinkConnectedTo(link.Index, selectedIndex);
            var pen = new Pen(
                selectedLink ? new SolidColorBrush(Color.Parse("#FFB35C")) :
                highlighted ? Brushes.White : new SolidColorBrush(Color.Parse("#68717D")),
                selectedLink ? 3.0 : highlighted ? 2.2 : 1.1);
            Point start = ToScreen(Graph.File.Nodes[link.NodeAIndex]);
            Point end = ToScreen(Graph.File.Nodes[link.NodeBIndex]);
            int anchorIndex = selectedLink && PreviewAnchorNodeIndex >= 0
                ? PreviewAnchorNodeIndex
                : link.AnchorNodeIndex;
            if (anchorIndex == ushort.MaxValue)
            {
                context.DrawLine(pen, start, end);
                continue;
            }

            if (anchorIndex >= Graph.File.Nodes.Count)
            {
                context.DrawLine(pen, start, end);
                continue;
            }
            Point anchor = ToScreen(Graph.File.Nodes[anchorIndex]);
            // The anchor is the center of the circular arc, not a point the line
            // passes through. Stock links place A and B at approximately the same
            // radius from this node. Sample the shorter arc explicitly so the chosen
            // center cannot be confused with the alternate circle through A and B.
            double startAngle = Math.Atan2(start.Y - anchor.Y, start.X - anchor.X);
            double endAngle = Math.Atan2(end.Y - anchor.Y, end.X - anchor.X);
            double delta = endAngle - startAngle;
            while (delta > Math.PI) delta -= Math.PI * 2;
            while (delta < -Math.PI) delta += Math.PI * 2;
            double startRadius = Math.Sqrt(
                Math.Pow(start.X - anchor.X, 2) + Math.Pow(start.Y - anchor.Y, 2));
            double endRadius = Math.Sqrt(
                Math.Pow(end.X - anchor.X, 2) + Math.Pow(end.Y - anchor.Y, 2));
            double radius = (startRadius + endRadius) / 2;
            int segments = Math.Max(8, (int)Math.Ceiling(Math.Abs(delta) * 12));
            var geometry = new StreamGeometry();
            using (StreamGeometryContext curve = geometry.Open())
            {
                curve.BeginFigure(start, false);
                for (int segment = 1; segment < segments; segment++)
                {
                    double angle = startAngle + delta * segment / segments;
                    curve.LineTo(new Point(
                        anchor.X + Math.Cos(angle) * radius,
                        anchor.Y + Math.Sin(angle) * radius));
                }
                curve.LineTo(end);
            }
            context.DrawGeometry(null, pen, geometry);
        }
    }

    private void DrawNodes(DrawingContext context)
    {
        if (Graph is null) return;
        SphereGridLink? selectedLink = SelectedLinkIndex >= 0 &&
            SelectedLinkIndex < Graph.File.Links.Count
                ? Graph.File.Links[SelectedLinkIndex]
                : null;
        foreach (SphereGridNode node in Graph.VisibleNodes)
        {
            Point center = ToScreen(node);
            bool selected = node.Index == SelectedNode?.Index;
            bool selectedEndpoint = selectedLink is not null &&
                (node.Index == selectedLink.NodeAIndex ||
                 node.Index == selectedLink.NodeBIndex);
            bool previewEndpoint = node.Index == PreviewLinkNodeAIndex ||
                                   node.Index == PreviewLinkNodeBIndex;
            double zoomSize = Math.Clamp(
                Math.Log2(Math.Max(0.01, ZoomPercent / 100.0)) / 3.0,
                -1,
                1);
            double radius = selected
                ? 6.5 + zoomSize * (zoomSize >= 0 ? 2 : 1)
                : 4.5 + zoomSize * (zoomSize >= 0 ? 1.5 : 0.75);
            SphereGridCharacter character = GetCharacter(node.Index);
            string routeColor = Graph.Routes.Palette[character].Color;
            context.DrawEllipse(
                new SolidColorBrush(Color.Parse(routeColor)),
                previewEndpoint
                    ? new Pen(new SolidColorBrush(Color.Parse("#FFB35C")), 2.8)
                    : selected || selectedEndpoint
                    ? new Pen(Brushes.White, selected ? 2 : 1.6)
                    : new Pen(Brushes.Black, 0.8),
                center, radius, radius);

            if (_transform.Scale >= 0.65)
            {
                var label = new FormattedText(
                    node.TypeInfo.ShortName,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Arial", FontStyle.Normal, FontWeight.SemiBold),
                    10 + zoomSize * (zoomSize >= 0 ? 2 : 1.5),
                    Brushes.White);
                context.DrawText(label, new Point(
                    center.X - label.Width / 2,
                    center.Y - radius - label.Height - 2));
            }
        }
    }

    private void DrawLinkCreationPreview(DrawingContext context)
    {
        if (Graph is null || PreviewLinkNodeAIndex < 0 || PreviewLinkNodeBIndex < 0 ||
            PreviewLinkNodeAIndex >= Graph.File.Nodes.Count ||
            PreviewLinkNodeBIndex >= Graph.File.Nodes.Count)
            return;

        var previewPen = new Pen(
            new SolidColorBrush(Color.Parse("#FFB35C")),
            3,
            dashStyle: new DashStyle(new[] { 5d, 4d }, 0));
        context.DrawLine(
            previewPen,
            ToScreen(Graph.File.Nodes[PreviewLinkNodeAIndex]),
            ToScreen(Graph.File.Nodes[PreviewLinkNodeBIndex]));
    }

    private Point ToScreen(SphereGridNode node) => _transform.ToScreen(node.X, node.Y);

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        EnsureTransform();
        Point pointer = e.GetPosition(this);
        SphereGridNode? node = FindNode(pointer);
        if (node is not null)
        {
            if (NodeSelectionRequested is not null)
                NodeSelectionRequested(this, node);
            else
                SelectedNode = node;
            if (IsLinkCreationMode)
            {
                ClearNodeDrag();
                InvalidateVisual();
                return;
            }
            _dragNodeIndex = node.Index;
            _dragStartPoint = pointer;
            _dragOffset = ToScreen(node) - pointer;
            _nodeDragStarted = false;
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }
        ClearNodeDrag();
        int linkIndex = FindLink(pointer);
        if (linkIndex >= 0)
        {
            LinkSelectionRequested?.Invoke(this, linkIndex);
            InvalidateVisual();
            return;
        }
        EmptySpaceSelectionRequested?.Invoke(this, EventArgs.Empty);
        _panning = true;
        _lastPanPoint = pointer;
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        Point pointer = e.GetPosition(this);
        if (_dragNodeIndex >= 0 &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Only move the node after the editor has accepted this selection.
            if (SelectedNode?.Index != _dragNodeIndex)
                return;
            if (!_nodeDragStarted)
            {
                if (Distance(pointer, _dragStartPoint) < 4)
                    return;
                _nodeDragStarted = true;
                NodeDragStarted?.Invoke(this, _dragNodeIndex);
            }

            Point target = pointer + _dragOffset;
            (double worldX, double worldY) = _transform.ToWorld(target);
            short x = (short)Math.Clamp(
                (int)Math.Round(worldX), short.MinValue, short.MaxValue);
            short y = (short)Math.Clamp(
                (int)Math.Round(worldY), short.MinValue, short.MaxValue);
            NodePositionPreviewRequested?.Invoke(
                this, new NodePositionPreviewEventArgs(_dragNodeIndex, x, y));
            ToolTip.SetTip(this, null);
            InvalidateVisual();
            return;
        }

        if (_panning && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Vector movement = pointer - _lastPanPoint;
            _transform = ClampToVisibleNodes(
                _transform.Pan(movement.X, movement.Y));
            _lastPanPoint = pointer;
            ToolTip.SetTip(this, null);
            InvalidateVisual();
            return;
        }

        SphereGridNode? hovered = FindNode(pointer);
        ToolTip.SetTip(this, hovered is null
            ? null
            : $"Node #{hovered.Index}\n{hovered.TypeInfo.Name}\n" +
              $"Section: {Graph!.Routes.Palette[GetCharacter(hovered.Index)].Name}\n" +
              $"Position: {hovered.X}, {hovered.Y}\nCluster: {hovered.ClusterIndex}");
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _panning = false;
        if (_nodeDragStarted && _dragNodeIndex >= 0)
            NodeDragCompleted?.Invoke(this, _dragNodeIndex);
        ClearNodeDrag();
        e.Pointer.Capture(null);
    }

    private void ClearNodeDrag()
    {
        _dragNodeIndex = -1;
        _nodeDragStarted = false;
    }

    private SphereGridNode? FindNode(Point pointer)
    {
        if (Graph is null || !_transform.IsValid)
            return null;
        double radius = Math.Max(8, Math.Min(15, 5 + _transform.Scale * 3));
        return Graph.VisibleNodes
            .Select(node => (Node: node, Distance: Distance(ToScreen(node), pointer)))
            .Where(candidate => candidate.Distance <= radius)
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => candidate.Node)
            .FirstOrDefault();
    }

    private int FindLink(Point pointer)
    {
        if (Graph is null || !_transform.IsValid)
            return -1;
        const double hitRadius = 8;
        return Graph.File.Links
            .Select(link => (link.Index, Distance: DistanceToLink(link, pointer)))
            .Where(candidate => candidate.Distance <= hitRadius)
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => candidate.Index)
            .FirstOrDefault(-1);
    }

    private double DistanceToLink(SphereGridLink link, Point pointer)
    {
        if (Graph is null || link.NodeAIndex >= Graph.File.Nodes.Count ||
            link.NodeBIndex >= Graph.File.Nodes.Count)
            return double.MaxValue;
        Point start = ToScreen(Graph.File.Nodes[link.NodeAIndex]);
        Point end = ToScreen(Graph.File.Nodes[link.NodeBIndex]);
        int anchorIndex = link.Index == SelectedLinkIndex && PreviewAnchorNodeIndex >= 0
            ? PreviewAnchorNodeIndex
            : link.AnchorNodeIndex;
        if (anchorIndex == ushort.MaxValue || anchorIndex >= Graph.File.Nodes.Count)
            return DistanceToSegment(pointer, start, end);

        Point anchor = ToScreen(Graph.File.Nodes[anchorIndex]);
        double startAngle = Math.Atan2(start.Y - anchor.Y, start.X - anchor.X);
        double endAngle = Math.Atan2(end.Y - anchor.Y, end.X - anchor.X);
        double delta = endAngle - startAngle;
        while (delta > Math.PI) delta -= Math.PI * 2;
        while (delta < -Math.PI) delta += Math.PI * 2;
        double startRadius = Distance(start, anchor);
        double endRadius = Distance(end, anchor);
        double radius = (startRadius + endRadius) / 2;
        int segments = Math.Max(8, (int)Math.Ceiling(Math.Abs(delta) * 12));
        double closest = double.MaxValue;
        Point previous = start;
        for (int segment = 1; segment <= segments; segment++)
        {
            Point next = segment == segments
                ? end
                : new Point(
                    anchor.X + Math.Cos(startAngle + delta * segment / segments) * radius,
                    anchor.Y + Math.Sin(startAngle + delta * segment / segments) * radius);
            closest = Math.Min(closest, DistanceToSegment(pointer, previous, next));
            previous = next;
        }
        return closest;
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        Vector segment = end - start;
        double lengthSquared = segment.X * segment.X + segment.Y * segment.Y;
        if (lengthSquared <= double.Epsilon)
            return Distance(point, start);
        Vector offset = point - start;
        double t = Math.Clamp(
            (offset.X * segment.X + offset.Y * segment.Y) / lengthSquared, 0, 1);
        return Distance(point, start + segment * t);
    }

    private SphereGridCharacter GetCharacter(int nodeIndex)
    {
        if (ColorOverrides?.TryGetValue(
                nodeIndex, out SphereGridCharacter character) == true)
            return character;
        return Graph?.Routes.GetCharacter(nodeIndex) ??
               SphereGridCharacter.Unassigned;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    private void ZoomAt(Point anchor, double factor)
    {
        EnsureTransform();
        if (!_transform.IsValid || _fitScale <= 0)
            return;
        double target = Math.Clamp(_transform.Scale * factor, _fitScale * 0.4, _fitScale * 8);
        _transform = _transform.ZoomAt(anchor, target);
        ZoomPercent = (int)Math.Round(target / _fitScale * 100);
        InvalidateVisual();
    }

    private GraphTransform ClampToVisibleNodes(GraphTransform transform)
    {
        if (Graph is null || Graph.VisibleNodes.Count == 0)
            return transform;

        Point[] screenNodes = Graph.VisibleNodes
            .Select(node => transform.ToScreen(node.X, node.Y))
            .ToArray();
        double minimumX = screenNodes.Min(point => point.X);
        double maximumX = screenNodes.Max(point => point.X);
        double minimumY = screenNodes.Min(point => point.Y);
        double maximumY = screenNodes.Max(point => point.Y);
        double viewportCenterX = Bounds.Width / 2;
        double viewportCenterY = Bounds.Height / 2;
        double adjustmentX = maximumX < viewportCenterX
            ? viewportCenterX - maximumX
            : minimumX > viewportCenterX
                ? viewportCenterX - minimumX
                : 0;
        double adjustmentY = maximumY < viewportCenterY
            ? viewportCenterY - maximumY
            : minimumY > viewportCenterY
                ? viewportCenterY - minimumY
                : 0;
        return transform.Pan(adjustmentX, adjustmentY);
    }

    private static double Distance(Point a, Point b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private readonly record struct GraphTransform(
        double MinimumX, double MinimumY, double Scale, double Left, double Top)
    {
        public bool IsValid => Scale > 0;

        public static GraphTransform Fit(SphereGridBounds graph, Rect viewport)
        {
            const double margin = 35;
            double width = Math.Max(1, graph.Width);
            double height = Math.Max(1, graph.Height);
            double scale = Math.Min(
                Math.Max(1, viewport.Width - margin * 2) / width,
                Math.Max(1, viewport.Height - margin * 2) / height);
            double contentWidth = width * scale;
            double contentHeight = height * scale;
            return new GraphTransform(
                graph.MinimumX,
                graph.MinimumY,
                scale,
                (viewport.Width - contentWidth) / 2,
                (viewport.Height - contentHeight) / 2);
        }

        public Point ToScreen(double x, double y) =>
            new(Left + (x - MinimumX) * Scale, Top + (y - MinimumY) * Scale);

        public (double X, double Y) ToWorld(Point point) =>
            (MinimumX + (point.X - Left) / Scale,
             MinimumY + (point.Y - Top) / Scale);

        public GraphTransform Pan(double x, double y) =>
            this with { Left = Left + x, Top = Top + y };

        public GraphTransform ZoomAt(Point anchor, double newScale)
        {
            (double worldX, double worldY) = ToWorld(anchor);
            return this with
            {
                Scale = newScale,
                Left = anchor.X - (worldX - MinimumX) * newScale,
                Top = anchor.Y - (worldY - MinimumY) * newScale
            };
        }
    }
}

public sealed class NodePositionPreviewEventArgs(
    int nodeIndex, short x, short y) : EventArgs
{
    public int NodeIndex { get; } = nodeIndex;
    public short X { get; } = x;
    public short Y { get; } = y;
}
