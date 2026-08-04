using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public static class GuideMapSvgRenderer
{
    public static string Render(
        GuideMapModel model,
        IEnumerable<ProjectedChestLocation>? chests = null,
        int width = 900,
        int height = 700)
    {
        GuideMapProjection projection = GuideMapProjection.Fit(model, width, height);
        var path = new StringBuilder();
        foreach (GuideMapTriangle triangle in model.Triangles)
        {
            GuideMapVertex a = model.Vertices[triangle.A];
            GuideMapVertex b = model.Vertices[triangle.B];
            GuideMapVertex c = model.Vertices[triangle.C];
            (float ax, float ay) = projection.Project(a.X, a.Z);
            (float bx, float by) = projection.Project(b.X, b.Z);
            (float cx, float cy) = projection.Project(c.X, c.Z);
            path.AppendFormat(CultureInfo.InvariantCulture,
                "M{0:0.##},{1:0.##}L{2:0.##},{3:0.##}L{4:0.##},{5:0.##}Z", ax, ay, bx, by, cx, cy);
        }
        var overlays = new StringBuilder();
        foreach (ProjectedChestLocation chest in chests ?? [])
        {
            if (!chest.PixelX.HasValue || !chest.PixelY.HasValue) continue;
            string label = chest.TreasureIds.Count == 1 ? chest.TreasureIds[0].ToString(CultureInfo.InvariantCulture) : "?";
            overlays.AppendFormat(CultureInfo.InvariantCulture,
                "<circle cx=\"{0:0.##}\" cy=\"{1:0.##}\" r=\"7\" fill=\"#ffcb55\" stroke=\"#281900\" stroke-width=\"2\"><title>Treasure {2}, {3} w{4:X2}</title></circle>\n",
                chest.PixelX.Value, chest.PixelY.Value, label, chest.EventId, chest.WorkerIndex);
        }
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">\n" +
               "<rect width=\"100%\" height=\"100%\" fill=\"#07121b\"/>\n" +
               $"<path d=\"{path}\" fill=\"#173b4a\" fill-opacity=\"0.35\" stroke=\"#5de6ff\" stroke-width=\"1.2\" stroke-linejoin=\"round\"/>\n" +
               overlays +
               "</svg>\n";
    }
}
