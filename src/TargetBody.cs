using Godot;

namespace HeadView;

public partial class TargetBody : StaticBody3D
{
    private StandardMaterial3D _material = null!;
    private float _flash;

    public override void _Ready()
    {
        var mesh = GetNode<MeshInstance3D>("Visual");
        _material = (StandardMaterial3D)mesh.MaterialOverride.Duplicate();
        mesh.MaterialOverride = _material;
    }

    public void Hit()
    {
        _flash = 0.25f;
        _material.AlbedoColor = new Color(1f, 1f, 0.55f);
        _material.Emission = new Color(0.8f, 0.8f, 0.15f);
    }

    public override void _Process(double delta)
    {
        if (_flash <= 0) return;
        _flash -= (float)delta;
        if (_flash > 0) return;
        _material.AlbedoColor = new Color(0.95f, 0.25f, 0.15f);
        _material.Emission = new Color(0.24f, 0.04f, 0.02f);
    }
}
