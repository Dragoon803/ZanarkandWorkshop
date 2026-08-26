using System;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public sealed record GuideMapProjection(
    float MinX, float MinZ, float Scale, float OffsetX, float OffsetY, int Width, int Height)
{
    public static GuideMapProjection Fit(GuideMapModel model, int width, int height, float padding = 24)
    {
        if (width <= padding * 2 || height <= padding * 2)
            throw new ArgumentOutOfRangeException(nameof(width), "Canvas is too small for its padding.");
        float extentX = Math.Max(0.001f, model.BoundsMax.X - model.BoundsMin.X);
        float extentZ = Math.Max(0.001f, model.BoundsMax.Z - model.BoundsMin.Z);
        float scale = Math.Min((width - padding * 2) / extentX, (height - padding * 2) / extentZ);
        float offsetX = (width - extentX * scale) / 2f;
        float offsetY = (height - extentZ * scale) / 2f;
        return new GuideMapProjection(model.BoundsMin.X, model.BoundsMin.Z, scale, offsetX, offsetY, width, height);
    }

    public (float X, float Y) Project(float guideX, float guideZ) =>
        (OffsetX + (guideX - MinX) * Scale, Height - OffsetY - (guideZ - MinZ) * Scale);

    public (float X, float Y) ProjectWorld(float worldX, float worldZ) =>
        Project(worldX / 10f, worldZ / 10f);
}
