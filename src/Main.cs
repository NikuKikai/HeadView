using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace HeadView;

public partial class Main : Node3D
{
    private enum ViewMode { Single, Dual }

    private const float MouseSensitivity = 0.0022f;
    private const float WalkSpeed = 5.5f;
    private const float RunSpeed = 8.5f;
    private const float JumpSpeed = 5.2f;
    private const float DefaultFocusRadius = 0.64f;

    [Export(PropertyHint.Range, "35,120,1")]
    public float HorizontalFovDeg { get; set; } = 70f;

    [Export(PropertyHint.Range, "0,20,0.1")]
    public float GapHorizontalDeg { get; set; } = 0f;

    [Export(PropertyHint.Range, "0.1,2,0.01")]
    public float ScreenPhysicalWidthMeters { get; set; } = 0.53f;

    [Export(PropertyHint.Range, "0.1,3,0.01")]
    public float HeadToScreenDistanceMeters { get; set; } = 0.5f;

    private CharacterBody3D _player = null!;
    private Node3D _head = null!;
    private SubViewportContainer _leftScreen = null!;
    private SubViewportContainer _rightScreen = null!;
    private SubViewport _leftViewport = null!;
    private SubViewport _rightViewport = null!;
    private Camera3D _leftCamera = null!;
    private Camera3D _rightCamera = null!;
    private ShaderMaterial _focus = null!;
    private SeamCrosshair _crosshair = null!;
    private Label _leftInfo = null!;
    private Label _rightInfo = null!;
    private Panel _facePanel = null!;
    private FaceLandmarksView _facePlot = null!;
    private TextureRect _facePreview = null!;
    private Label _faceValues = null!;
    private Label3D _sensitivityValue = null!;
    private Label3D _mouseSensitivityValue = null!;
    private Label3D _radiusValue = null!;
    private Label3D _previewValue = null!;
    private SettingButton3D _previewToggleButton = null!;
    private Label3D _screenModeValue = null!;
    private SettingButton3D _screenModeToggleButton = null!;
    private Node3D _settingsPanel = null!;
    private ImageTexture? _facePreviewTexture;
    private byte[]? _lastPreviewJpeg;
    private HeadTracker? _tracker;
    private Rect2I _leftDisplay;
    private Rect2I _rightDisplay;
    private Vector2 _smoothedHead;
    private float _headSensitivity = 1f;
    private float _mouseSensitivity = 1f;
    private float _focusRadius = DefaultFocusRadius;
    private float _pitch;
    private bool _blurEnabled = true;
    private bool _showCameraPreview = true;
    private bool _hasDualScreens;
    private bool _changingWindowLayout;
    private ViewMode _viewMode;
    private bool _captureQa;
    private int _hits;
    private int _qaFrames;
    private Vector2I _lastLeftViewportSize;
    private Vector2I _lastRightViewportSize;

    public override void _Ready()
    {
        _player = GetNode<CharacterBody3D>("Player");
        _head = GetNode<Node3D>("Player/HeadCenter");
        _leftScreen = GetNode<SubViewportContainer>("ScreenLayout/LeftScreen");
        _rightScreen = GetNode<SubViewportContainer>("ScreenLayout/RightScreen");
        _leftViewport = GetNode<SubViewport>("ScreenLayout/LeftScreen/LeftViewport");
        _rightViewport = GetNode<SubViewport>("ScreenLayout/RightScreen/RightViewport");
        _leftCamera = GetNode<Camera3D>("ScreenLayout/LeftScreen/LeftViewport/LeftCamera");
        _rightCamera = GetNode<Camera3D>("ScreenLayout/RightScreen/RightViewport/RightCamera");
        _focus = (ShaderMaterial)GetNode<ColorRect>("Hud/FocusEffect").Material;
        _crosshair = GetNode<SeamCrosshair>("Hud/Crosshair");
        _leftInfo = GetNode<Label>("Hud/LeftInfo");
        _rightInfo = GetNode<Label>("Hud/RightInfo");
        _facePanel = GetNode<Panel>("Hud/FacePanel");
        _facePlot = GetNode<FaceLandmarksView>("Hud/FacePanel/FacePreview/FaceOverlay");
        _facePreview = GetNode<TextureRect>("Hud/FacePanel/FacePreview/CameraImage");
        _facePreview.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        _faceValues = GetNode<Label>("Hud/FacePanel/Values");
        _sensitivityValue = GetNode<Label3D>("SettingsPanel/SensitivityValue");
        _mouseSensitivityValue = GetNode<Label3D>("SettingsPanel/MouseValue");
        _radiusValue = GetNode<Label3D>("SettingsPanel/RadiusValue");
        _previewValue = GetNode<Label3D>("SettingsPanel/PreviewValue");
        _previewToggleButton = GetNode<SettingButton3D>("SettingsPanel/PreviewToggle");
        _screenModeValue = GetNode<Label3D>("SettingsPanel/ScreenModeValue");
        _screenModeToggleButton = GetNode<SettingButton3D>("SettingsPanel/ScreenModeToggle");
        _settingsPanel = GetNode<Node3D>("SettingsPanel");
        _captureQa = OS.GetCmdlineUserArgs().Contains("--capture-qa");

        World3D sharedWorld = GetViewport().World3D;
        _leftViewport.World3D = sharedWorld;
        _rightViewport.World3D = sharedWorld;
        _leftCamera.MakeCurrent();
        _rightCamera.MakeCurrent();
        ConfigureWindow();
        GetWindow().SizeChanged += OnWindowSizeChanged;
        UpdateSettingLabels();
        StartHeadTracker();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void ConfigureWindow()
    {
        _hasDualScreens = DisplayServer.GetScreenCount() >= 2;
        _viewMode = _hasDualScreens ? ViewMode.Dual : ViewMode.Single;
        ApplyViewModeWindow();
    }

    private void SetViewMode(ViewMode mode)
    {
        _hasDualScreens = DisplayServer.GetScreenCount() >= 2;
        if (mode == ViewMode.Dual && !_hasDualScreens) return;
        if (_viewMode == mode) return;
        _viewMode = mode;
        ApplyViewModeWindow();
        UpdateSettingLabels();
    }

    private void ApplyViewModeWindow()
    {
        Window window = GetWindow();
        int count = DisplayServer.GetScreenCount();
        _hasDualScreens = count >= 2;
        if (!_hasDualScreens) _viewMode = ViewMode.Single;
        window.Title = _viewMode == ViewMode.Dual ? "HeadView | Dual Screen" : "HeadView | Single Screen";
        _changingWindowLayout = true;
        try
        {
            window.Mode = Window.ModeEnum.Windowed;
            window.Borderless = true;
            if (_viewMode == ViewMode.Dual)
            {
                List<int> screens = Enumerable.Range(0, count)
                    .OrderBy(s => DisplayServer.ScreenGetPosition(s).X).ToList();
                Vector2I leftPosition = DisplayServer.ScreenGetPosition(screens[0]);
                Vector2I rightPosition = DisplayServer.ScreenGetPosition(screens[1]);
                Vector2I leftSize = DisplayServer.ScreenGetSize(screens[0]);
                Vector2I rightSize = DisplayServer.ScreenGetSize(screens[1]);
                Vector2I origin = new(Math.Min(leftPosition.X, rightPosition.X), Math.Min(leftPosition.Y, rightPosition.Y));
                Vector2I end = new(Math.Max(leftPosition.X + leftSize.X, rightPosition.X + rightSize.X),
                    Math.Max(leftPosition.Y + leftSize.Y, rightPosition.Y + rightSize.Y));
                window.Size = end - origin;
                window.Position = origin;
                _leftDisplay = new Rect2I(leftPosition - origin, leftSize);
                _rightDisplay = new Rect2I(rightPosition - origin, rightSize);
            }
            else
            {
                int primaryScreen = Math.Clamp(DisplayServer.GetPrimaryScreen(), 0, Math.Max(0, count - 1));
                Vector2I screenPosition = DisplayServer.ScreenGetPosition(primaryScreen);
                Vector2I screenSize = DisplayServer.ScreenGetSize(primaryScreen);
                window.Size = screenSize;
                window.Position = screenPosition;
                _leftDisplay = new Rect2I(Vector2I.Zero, screenSize);
                _rightDisplay = new Rect2I(Vector2I.Zero, Vector2I.One);
            }
        }
        finally
        {
            _changingWindowLayout = false;
        }
        ApplyLayout();
        UpdateCameras();
    }

    private void OnWindowSizeChanged()
    {
        if (_changingWindowLayout) return;
        if (_viewMode == ViewMode.Single)
        {
            _leftDisplay = new Rect2I(Vector2I.Zero, GetWindow().Size);
        }
        ApplyLayout();
        UpdateCameras();
    }

    private void ApplyLayout()
    {
        PlaceScreen(_leftScreen, _leftDisplay);
        _rightScreen.Visible = _viewMode == ViewMode.Dual;
        if (_viewMode == ViewMode.Dual)
            PlaceScreen(_rightScreen, _rightDisplay);
        _leftInfo.Position = new Vector2(_leftDisplay.Position.X + 20, _leftDisplay.Position.Y + 18);
        _rightInfo.Visible = _viewMode == ViewMode.Dual;
        Rect2I hudDisplay = _viewMode == ViewMode.Dual ? _rightDisplay : _leftDisplay;
        if (_viewMode == ViewMode.Dual)
        {
            _rightInfo.Position = new Vector2(hudDisplay.Position.X + 20, hudDisplay.Position.Y + 18);
            _facePanel.Position = new Vector2(hudDisplay.Position.X + 20, hudDisplay.Position.Y + 62);
        }
        else
        {
            _facePanel.Position = new Vector2(hudDisplay.Position.X + hudDisplay.Size.X - _facePanel.Size.X - 20,
                hudDisplay.Position.Y + 62);
        }
        _settingsPanel.Position = _viewMode == ViewMode.Dual ?
            new Vector3(-3f, 1.85f, -1f) : new Vector3(-1f, 1.85f, -1f);
        _crosshair.AimCenter = GetSeamCenter();
        _crosshair.QueueRedraw();
    }

    private static void PlaceScreen(SubViewportContainer container, Rect2I rect)
    {
        container.Position = new Vector2(rect.Position.X, rect.Position.Y);
        container.Size = new Vector2(rect.Size.X, rect.Size.Y);
    }

    private Vector2 GetSeamCenter()
    {
        if (_viewMode == ViewMode.Single)
            return new Vector2(_leftDisplay.Position.X + _leftDisplay.Size.X * 0.5f,
                _leftDisplay.Position.Y + _leftDisplay.Size.Y * 0.5f);
        Vector2 leftInner = new(_leftDisplay.Position.X + _leftDisplay.Size.X,
            _leftDisplay.Position.Y + _leftDisplay.Size.Y * 0.5f);
        Vector2 rightInner = new(_rightDisplay.Position.X,
            _rightDisplay.Position.Y + _rightDisplay.Size.Y * 0.5f);
        return (leftInner + rightInner) * 0.5f;
    }

    private void StartHeadTracker()
    {
        byte[] landmarkerBytes = Godot.FileAccess.GetFileAsBytes("res://assets/face_landmarker.task");
        if (landmarkerBytes.Length < 1_000_000)
        {
            GD.PushError("MediaPipe Tasks model is missing. Head tracking cannot start.");
            return;
        }
        string landmarker = ProjectSettings.GlobalizePath("user://headview-face-landmarker.task");
        System.IO.File.WriteAllBytes(landmarker, landmarkerBytes);
        _tracker = new HeadTracker(landmarker);
        int cameraIndex = (int)ProjectSettings.GetSetting("headview/camera_index", 0);
        int captureWidth = (int)ProjectSettings.GetSetting("headview/capture_width", 1280);
        int captureHeight = (int)ProjectSettings.GetSetting("headview/capture_height", 720);
        _tracker.Start(cameraIndex, captureWidth, captureHeight);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _player.RotateY(-motion.Relative.X * MouseSensitivity * _mouseSensitivity);
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity * _mouseSensitivity, -1.35f, 1.35f);
            _head.Rotation = new Vector3(_pitch, 0, 0);
        }
        else if (@event is InputEventMouseButton button && button.Pressed)
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured)
                Input.MouseMode = Input.MouseModeEnum.Captured;
            else if (button.ButtonIndex == MouseButton.Left)
                Shoot();
        }
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.Escape:
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    break;
                case Key.Backspace:
                    GetTree().Quit();
                    break;
                case Key.C:
                    _tracker?.Recenter();
                    break;
                case Key.Bracketleft:
                    GapHorizontalDeg = Mathf.Max(0f, GapHorizontalDeg - 0.5f);
                    UpdateCameras();
                    break;
                case Key.Bracketright:
                    GapHorizontalDeg = Mathf.Min(20f, GapHorizontalDeg + 0.5f);
                    UpdateCameras();
                    break;
                case Key.B:
                    _blurEnabled = !_blurEnabled;
                    break;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var movement = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W)) movement.Y -= 1;
        if (Input.IsKeyPressed(Key.S)) movement.Y += 1;
        if (Input.IsKeyPressed(Key.A)) movement.X -= 1;
        if (Input.IsKeyPressed(Key.D)) movement.X += 1;
        movement = movement.Normalized();
        Vector3 direction = _player.Basis * new Vector3(movement.X, 0, movement.Y);
        float speed = Input.IsKeyPressed(Key.Shift) ? RunSpeed : WalkSpeed;
        Vector3 velocity = _player.Velocity;
        velocity.X = direction.X * speed;
        velocity.Z = direction.Z * speed;
        if (!_player.IsOnFloor()) velocity.Y -= 17f * (float)delta;
        else if (Input.IsKeyPressed(Key.Space)) velocity.Y = JumpSpeed;
        else velocity.Y = 0;
        _player.Velocity = velocity;
        _player.MoveAndSlide();
    }

    public override void _Process(double delta)
    {
        if (_lastLeftViewportSize != _leftViewport.Size || _lastRightViewportSize != _rightViewport.Size)
            UpdateCameras();
        UpdateCameraTransforms();
        FaceTrackingSnapshot tracking = _tracker?.Snapshot ?? new FaceTrackingSnapshot(
            false, "Head model unavailable", Vector3.Zero, Array.Empty<Vector2>(), Array.Empty<byte>());
        Vector2 target = new(Mathf.DegToRad(tracking.Orientation.X) * _headSensitivity,
            Mathf.DegToRad(tracking.Orientation.Y) * _headSensitivity);
        _smoothedHead = _smoothedHead.Lerp(target, 1f - Mathf.Exp(-8f * (float)delta));
        UpdateFocus();
        _facePlot.SetSnapshot(tracking);
        if (_showCameraPreview)
            UpdateFacePreview(tracking);
        _faceValues.Text = tracking.HasFace
            ? $"Yaw {tracking.Orientation.X:+0.0;-0.0;0.0}°  Pitch {tracking.Orientation.Y:+0.0;-0.0;0.0}°\nRoll {tracking.Orientation.Z:+0.0;-0.0;0.0}°\nPose: MediaPipe matrix"
            : "No face landmarks\nPose: MediaPipe matrix";
        string gapHint = _viewMode == ViewMode.Dual ? "  [ / ] gap" : "";
        _leftInfo.Text = $"HEADVIEW  |  {_viewMode.ToString().ToUpperInvariant()}\nWASD move  Shift run  Space jump  Click shoot\nShoot 3D controls to adjust settings  C recalibrate{gapHint}\nB blur  Esc cursor  Backspace quit  |  Hits {_hits}  {tracking.Status}";
        _rightInfo.Text = $"RIGHT  |  Horizontal FOV {HorizontalFovDeg:0}°";
        if (_captureQa && ++_qaFrames == 300)
            CaptureQaFrame();
    }

    private void UpdateFacePreview(FaceTrackingSnapshot tracking)
    {
        if (tracking.PreviewJpeg.Length == 0)
        {
            _facePreview.Texture = null;
            _lastPreviewJpeg = null;
            return;
        }
        if (ReferenceEquals(_lastPreviewJpeg, tracking.PreviewJpeg)) return;
        _lastPreviewJpeg = tracking.PreviewJpeg;
        using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgb8);
        if (image.LoadJpgFromBuffer(tracking.PreviewJpeg) != Error.Ok) return;
        if (_facePreviewTexture == null)
            _facePreviewTexture = ImageTexture.CreateFromImage(image);
        else
            _facePreviewTexture.Update(image);
        _facePreview.Texture = _facePreviewTexture;
    }

    private void CaptureQaFrame()
    {
        Shoot();
        GD.Print($"QA window: position={GetWindow().Position}, size={GetWindow().Size}, visible={GetWindow().GetVisibleRect().Size}, texture={GetWindow().GetTexture().GetSize()}; left={_leftDisplay}, right={_rightDisplay}; subviewport sizes={_leftViewport.Size}/{_rightViewport.Size}");
        GD.Print($"QA focus={_focus.GetShaderParameter("focus_center")}; aim hits={_hits}; tracker={_tracker?.Snapshot.Status}; face panel={_facePanel.Position}/{_facePanel.Size}");
        ValidatePhysicalProjection();
        float originalPitch = _pitch;
        _pitch = 0.6f;
        UpdateCameraTransforms();
        float leftRoll = Mathf.Abs(_leftCamera.GlobalBasis.X.Y);
        float rightRoll = Mathf.Abs(_rightCamera.GlobalBasis.X.Y);
        GD.Print($"QA camera roll at 0.6 rad pitch: left={leftRoll:0.000000}, right={rightRoll:0.000000}");
        if (leftRoll > 0.001f || rightRoll > 0.001f)
            GD.PushError("Camera roll validation failed.");
        _pitch = originalPitch;
        UpdateCameraTransforms();
        GetWindow().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://qa-wide.png"));
        _leftViewport.GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://qa-left.png"));
        _rightViewport.GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://qa-right.png"));
        using Image previewTest = Image.CreateEmpty(256, 256, false, Image.Format.Rgb8);
        previewTest.Fill(new Color(0.2f, 0.6f, 0.4f));
        UpdateFacePreview(new FaceTrackingSnapshot(true, "QA preview", Vector3.Zero,
            Array.Empty<Vector2>(), previewTest.SaveJpgToBuffer()));
        if (_facePreview.Texture == null)
            GD.PushError("Face preview decoding validation failed.");
    }

    private void ValidatePhysicalProjection()
    {
        float halfWidth = _viewMode == ViewMode.Single ? ScreenPhysicalWidthMeters * 0.5f : ScreenPhysicalWidthMeters;
        float edgeAngle = Mathf.Atan(halfWidth / HeadToScreenDistanceMeters);
        Vector2 center = ProjectHeadDirectionToScreen(Vector2.Zero);
        Vector2 leftEdge = ProjectHeadDirectionToScreen(new Vector2(-edgeAngle, 0));
        Vector2 rightEdge = ProjectHeadDirectionToScreen(new Vector2(edgeAngle, 0));
        GD.Print($"QA physical focus ({_viewMode}): center={center}, left outer edge={leftEdge}, right outer edge={rightEdge}");
        Rect2I rightmostDisplay = _viewMode == ViewMode.Single ? _leftDisplay : _rightDisplay;
        if (Mathf.Abs(center.X - GetSeamCenter().X) > 1f ||
            Mathf.Abs(leftEdge.X - _leftDisplay.Position.X) > 1f ||
            Mathf.Abs(rightEdge.X - (rightmostDisplay.Position.X + rightmostDisplay.Size.X)) > 1f)
            GD.PushError("Physical screen projection validation failed.");
    }

    private void UpdateCameras()
    {
        if (_leftCamera == null || _rightCamera == null) return;
        _lastLeftViewportSize = _leftViewport.Size;
        _lastRightViewportSize = _rightViewport.Size;
        ConfigureCamera(_leftCamera, _lastLeftViewportSize);
        ConfigureCamera(_rightCamera, _lastRightViewportSize);
        UpdateCameraTransforms();
    }

    private void UpdateCameraTransforms()
    {
        if (_leftCamera == null || _rightCamera == null || _head == null) return;
        float halfSeparation = _viewMode == ViewMode.Dual ?
            Mathf.DegToRad((HorizontalFovDeg + GapHorizontalDeg) * 0.5f) : 0f;
        Basis body = _player.GlobalBasis;
        Basis pitch = new(Vector3.Right, _pitch);
        Vector3 eyePosition = _head.GlobalPosition;
        _leftCamera.GlobalTransform = new Transform3D(body * new Basis(Vector3.Up, halfSeparation) * pitch, eyePosition);
        _rightCamera.GlobalTransform = new Transform3D(body * new Basis(Vector3.Up, -halfSeparation) * pitch, eyePosition);
    }

    private void ConfigureCamera(Camera3D camera, Vector2I viewportSize)
    {
        float aspect = (float)Math.Max(1, viewportSize.X) / Math.Max(1, viewportSize.Y);
        float verticalFov = Mathf.RadToDeg(2f * Mathf.Atan(Mathf.Tan(Mathf.DegToRad(HorizontalFovDeg) * 0.5f) / aspect));
        camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        camera.Fov = verticalFov;
    }

    private void UpdateFocus()
    {
        Vector2 focusPoint = ProjectHeadDirectionToScreen(_smoothedHead);

        Vector2 size = GetWindow().Size;
        _focus.SetShaderParameter("focus_center", new Vector2(focusPoint.X / Math.Max(1f, size.X),
            focusPoint.Y / Math.Max(1f, size.Y)));
        _focus.SetShaderParameter("focus_radius", _focusRadius);
        _focus.SetShaderParameter("focus_visible", _blurEnabled);
        _focus.SetShaderParameter("blur_lod", _blurEnabled ? 5.5f : 0.0f);
    }

    private Vector2 ProjectHeadDirectionToScreen(Vector2 directionRadians)
    {
        float yaw = Mathf.Clamp(directionRadians.X, -1.45f, 1.45f);
        float pitch = Mathf.Clamp(directionRadians.Y, -1.2f, 1.2f);
        float distance = Mathf.Max(HeadToScreenDistanceMeters, 0.01f);
        float width = Mathf.Max(ScreenPhysicalWidthMeters, 0.01f);
        float xMeters = distance * Mathf.Tan(yaw);
        float yMeters = distance * Mathf.Tan(pitch) / Mathf.Cos(yaw);
        Rect2I display = _viewMode == ViewMode.Single ? _leftDisplay :
            xMeters < 0f ? _leftDisplay : _rightDisplay;
        float horizontal = _viewMode == ViewMode.Single ? 0.5f + xMeters / width :
            xMeters < 0f ? 1f + xMeters / width : xMeters / width;
        float screenHeightMeters = width * display.Size.Y / Mathf.Max(1, display.Size.X);
        float vertical = 0.5f - yMeters / screenHeightMeters;
        return new Vector2(display.Position.X + Mathf.Clamp(horizontal, 0f, 1f) * display.Size.X,
            display.Position.Y + Mathf.Clamp(vertical, 0f, 1f) * display.Size.Y);
    }

    private void Shoot()
    {
        var space = GetWorld3D().DirectSpaceState;
        Vector3 origin = _head.GlobalPosition;
        Vector3 end = origin - _head.GlobalBasis.Z * 100f;
        var query = PhysicsRayQueryParameters3D.Create(origin, end);
        query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
        var result = space.IntersectRay(query);
        if (!result.TryGetValue("collider", out Variant collider)) return;
        GodotObject hit = collider.AsGodotObject();
        if (hit is SettingButton3D button)
        {
            button.Hit();
            ApplySettingShot(button.Name.ToString());
        }
        else if (hit is TargetBody target)
        {
            target.Hit();
            _hits++;
        }
    }

    private void ApplySettingShot(string buttonName)
    {
        switch (buttonName)
        {
            case "SensitivityMinus":
                _headSensitivity = Mathf.Max(0.1f, Mathf.Round((_headSensitivity - 0.1f) * 10f) / 10f);
                break;
            case "SensitivityPlus":
                _headSensitivity = Mathf.Min(3f, Mathf.Round((_headSensitivity + 0.1f) * 10f) / 10f);
                break;
            case "MouseMinus":
                _mouseSensitivity = Mathf.Max(0.1f, Mathf.Round((_mouseSensitivity - 0.1f) * 10f) / 10f);
                break;
            case "MousePlus":
                _mouseSensitivity = Mathf.Min(3f, Mathf.Round((_mouseSensitivity + 0.1f) * 10f) / 10f);
                break;
            case "RadiusMinus":
                _focusRadius = Mathf.Max(0.10f, Mathf.Round((_focusRadius - 0.05f) * 100f) / 100f);
                break;
            case "RadiusPlus":
                _focusRadius = Mathf.Min(1.25f, Mathf.Round((_focusRadius + 0.05f) * 100f) / 100f);
                break;
            case "PreviewToggle":
                SetCameraPreviewVisible(!_showCameraPreview);
                break;
            case "ScreenModeToggle":
                SetViewMode(_viewMode == ViewMode.Dual ? ViewMode.Single : ViewMode.Dual);
                break;
        }
        UpdateSettingLabels();
    }

    private void SetCameraPreviewVisible(bool visible)
    {
        if (_showCameraPreview == visible) return;
        _showCameraPreview = visible;
        _facePreview.Visible = visible;
        _lastPreviewJpeg = null;
    }

    private void UpdateSettingLabels()
    {
        _sensitivityValue.Text = $"{_headSensitivity:0.0}x";
        _mouseSensitivityValue.Text = $"{_mouseSensitivity:0.0}x";
        _radiusValue.Text = $"{_focusRadius:0.00}";
        _previewValue.Text = _showCameraPreview ? "ON" : "OFF";
        _previewToggleButton.SetToggled(_showCameraPreview);
        _screenModeValue.Text = _viewMode == ViewMode.Dual ? "DUAL" : "SINGLE";
        _screenModeToggleButton.SetToggled(_viewMode == ViewMode.Dual);
        _screenModeToggleButton.CollisionLayer = _hasDualScreens ? 1u : 0u;
    }

    public override void _ExitTree()
    {
        _tracker?.Dispose();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }
}
