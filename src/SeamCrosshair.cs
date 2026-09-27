using Godot;

namespace HeadView;

public partial class SeamCrosshair : Control
{
    [Export]
    public Vector2 AimCenter { get; set; } = new Vector2(-1, -1);

    public override void _Draw()
    {
        var center = AimCenter.X < 0 ? Size * 0.5f : AimCenter;
        DrawArc(center, 38f, 0f, Mathf.Tau, 96, new Color(0.02f, 0.03f, 0.04f, 0.85f), 7f, true);
        DrawArc(center, 38f, 0f, Mathf.Tau, 96, new Color(1f, 0.93f, 0.55f, 0.95f), 3f, true);
        DrawCircle(center, 3f, new Color(1f, 0.93f, 0.55f, 0.95f));
    }
}
