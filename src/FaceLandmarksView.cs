using Godot;

namespace HeadView;

public partial class FaceLandmarksView : Control
{
    private FaceTrackingSnapshot _snapshot;

    public void SetSnapshot(FaceTrackingSnapshot snapshot)
    {
        _snapshot = snapshot;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_snapshot.HasFace || _snapshot.Landmarks.Length != 5)
            return;

        Color shadow = new(0f, 0f, 0f, 0.85f);
        Color active = new(0.28f, 0.95f, 0.75f, 1f);
        Vector2[] points = new Vector2[5];
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 landmark = _snapshot.Landmarks[i];
            points[i] = new Vector2(
                Mathf.Clamp(landmark.X, 0f, 1f) * Size.X,
                Mathf.Clamp(landmark.Y, 0f, 1f) * Size.Y);
        }

        DrawFaceLines(points, shadow, 4f);
        DrawFaceLines(points, active, 2f);
        foreach (Vector2 point in points)
        {
            DrawCircle(point, 6f, shadow);
            DrawCircle(point, 4f, active);
        }

        Vector2 arrowOrigin = new(Size.X - 33f, 33f);
        DrawCircle(arrowOrigin, 28f, new Color(0f, 0f, 0f, 0.65f));
        DrawLine(arrowOrigin - new Vector2(20, 0), arrowOrigin + new Vector2(20, 0), active, 1f);
        DrawLine(arrowOrigin - new Vector2(0, 20), arrowOrigin + new Vector2(0, 20), active, 1f);
        Vector2 arrowTip = arrowOrigin + new Vector2(
            Mathf.Clamp(_snapshot.Orientation.X * 0.7f, -20f, 20f),
            Mathf.Clamp(_snapshot.Orientation.Y * 0.7f, -20f, 20f));
        DrawLine(arrowOrigin, arrowTip, active, 3f);
        DrawCircle(arrowTip, 4f, active);
    }

    private void DrawFaceLines(Vector2[] points, Color color, float width)
    {
        DrawLine(points[0], points[1], color, width);
        DrawLine(points[0], points[2], color, width);
        DrawLine(points[1], points[2], color, width);
        DrawLine(points[2], points[3], color, width);
        DrawLine(points[2], points[4], color, width);
        DrawLine(points[3], points[4], color, width);
    }
}
