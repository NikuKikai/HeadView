using Godot;

namespace HeadView;

public partial class SettingButton3D : StaticBody3D
{
    private StandardMaterial3D _material = null!;
    private float _flash;
    private bool? _toggled;

    public override void _Ready()
    {
        var mesh = GetNode<MeshInstance3D>("Visual");
        _material = (StandardMaterial3D)mesh.MaterialOverride.Duplicate();
        mesh.MaterialOverride = _material;
        RestoreColor();
    }

    public void SetToggled(bool enabled)
    {
        _toggled = enabled;
        if (_flash <= 0f) RestoreColor();
    }

    public void Hit()
    {
        _flash = 0.18f;
        _material.AlbedoColor = new Color(0.95f, 0.96f, 0.50f);
        _material.Emission = new Color(0.72f, 0.75f, 0.22f);
    }

    public override void _Process(double delta)
    {
        if (_flash <= 0f) return;
        _flash -= (float)delta;
        if (_flash > 0f) return;
        RestoreColor();
    }

    private void RestoreColor()
    {
        if (_toggled == true)
        {
            _material.AlbedoColor = new Color(0.20f, 0.63f, 0.36f);
            _material.Emission = new Color(0.06f, 0.30f, 0.13f);
        }
        else if (_toggled == false)
        {
            _material.AlbedoColor = new Color(0.30f, 0.34f, 0.39f);
            _material.Emission = new Color(0.08f, 0.09f, 0.11f);
        }
        else
        {
            _material.AlbedoColor = new Color(0.11f, 0.50f, 0.61f);
            _material.Emission = new Color(0.04f, 0.24f, 0.32f);
        }
    }
}
