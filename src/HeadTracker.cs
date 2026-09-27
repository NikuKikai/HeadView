using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using OpenCvSharp;

namespace HeadView;

public readonly record struct FaceTrackingSnapshot(
    bool HasFace,
    string Status,
    Vector3 Orientation,
    Vector2[] Landmarks,
    byte[] PreviewJpeg);

public sealed class HeadTracker : IDisposable
{
    private readonly object _gate = new();
    private readonly string _landmarkerPath;
    private CancellationTokenSource? _cancel;
    private Task? _worker;
    private Vector3 _orientation;
    private Vector3 _neutralOrientation;
    private Vector2[] _landmarks = Array.Empty<Vector2>();
    private byte[] _previewJpeg = Array.Empty<byte>();
    private bool _hasFace;
    private string _status = "Camera starting";

    public HeadTracker(string landmarkerPath) => _landmarkerPath = landmarkerPath;

    public FaceTrackingSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return new FaceTrackingSnapshot(_hasFace, _status,
                    _hasFace ? _orientation - _neutralOrientation : Vector3.Zero,
                    _hasFace ? _landmarks : Array.Empty<Vector2>(), _previewJpeg);
        }
    }

    public void Start(int cameraIndex, int requestedWidth, int requestedHeight)
    {
        _cancel = new CancellationTokenSource();
        _worker = Task.Run(() => CaptureLoop(cameraIndex, requestedWidth, requestedHeight, _cancel.Token));
    }

    public void Recenter()
    {
        lock (_gate)
        {
            if (_hasFace) _neutralOrientation = _orientation;
        }
    }

    private void CaptureLoop(int cameraIndex, int requestedWidth, int requestedHeight, CancellationToken token)
    {
        try
        {
            using var capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
            if (!capture.IsOpened())
            {
                SetStatus("Camera unavailable; check Windows privacy settings");
                return;
            }
            capture.FrameWidth = requestedWidth;
            capture.FrameHeight = requestedHeight;
            using var frame = new Mat();
            for (int attempt = 0; attempt < 20 && !token.IsCancellationRequested; attempt++)
            {
                if (capture.Read(frame) && !frame.Empty()) break;
                Thread.Sleep(50);
            }
            if (frame.Empty())
            {
                SetStatus("Camera opened but returned no frames");
                return;
            }

            using var landmarker = new MediaPipeFaceLandmarker(_landmarkerPath);
            var clock = Stopwatch.StartNew();
            string resolution = $"{frame.Width}x{frame.Height}";
            GD.Print($"HeadView camera {cameraIndex}: requested {requestedWidth}x{requestedHeight}, actual {resolution}");
            SetStatus($"Searching for face ({resolution})");

            while (!token.IsCancellationRequested)
            {
                if (frame.Empty())
                {
                    SetFaceMissing($"Camera frame unavailable ({resolution})", Array.Empty<byte>());
                    Thread.Sleep(25);
                }
                else if (landmarker.TryDetect(frame, clock.ElapsedMilliseconds,
                             out Vector3 orientation, out Point2f[] points))
                {
                    Rect crop = CreateFaceCrop(frame, points);
                    PublishFace(orientation, points, resolution, CreateFacePreview(frame, crop), crop);
                }
                else
                {
                    SetFaceMissing($"Face not found ({resolution})", CreateFullFramePreview(frame));
                }

                if (!capture.Read(frame)) frame.Release();
            }
        }
        catch (Exception ex)
        {
            SetStatus("Head tracker error: " + ex.Message);
        }
    }

    private static Rect CreateFaceCrop(Mat frame, Point2f[] points)
    {
        float minX = frame.Width;
        float minY = frame.Height;
        float maxX = 0;
        float maxY = 0;
        foreach (Point2f point in points)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) continue;
            minX = MathF.Min(minX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxX = MathF.Max(maxX, point.X);
            maxY = MathF.Max(maxY, point.Y);
        }
        int side = Math.Clamp((int)MathF.Ceiling(MathF.Max(maxX - minX, maxY - minY) * 1.25f),
            96, Math.Min(frame.Width, frame.Height));
        int left = Math.Clamp((int)((minX + maxX - side) * 0.5f), 0, frame.Width - side);
        int top = Math.Clamp((int)((minY + maxY - side) * 0.5f), 0, frame.Height - side);
        return new Rect(left, top, side, side);
    }

    private static byte[] CreateFacePreview(Mat frame, Rect roi)
    {
        using var crop = new Mat(frame, roi);
        using var preview = new Mat();
        Cv2.Resize(crop, preview, new OpenCvSharp.Size(256, 256));
        Cv2.Flip(preview, preview, FlipMode.Y);
        return preview.ImEncode(".jpg");
    }

    private static byte[] CreateFullFramePreview(Mat frame)
    {
        int width = 256;
        int height = Math.Max(1, (int)MathF.Round(frame.Height * width / (float)frame.Width));
        if (height > 256)
        {
            height = 256;
            width = Math.Max(1, (int)MathF.Round(frame.Width * height / (float)frame.Height));
        }
        using var preview = new Mat();
        Cv2.Resize(frame, preview, new OpenCvSharp.Size(width, height));
        using var square = new Mat(new OpenCvSharp.Size(256, 256), MatType.CV_8UC3, Scalar.All(0));
        using (var target = new Mat(square, new Rect((256 - width) / 2, (256 - height) / 2, width, height)))
            preview.CopyTo(target);
        Cv2.Flip(square, square, FlipMode.Y);
        return square.ImEncode(".jpg");
    }

    private void PublishFace(Vector3 orientation, Point2f[] points, string resolution,
        byte[] previewJpeg, Rect crop)
    {
        Vector2[] landmarks =
        {
            NormalizeLandmark(points[33], crop),
            NormalizeLandmark(points[263], crop),
            NormalizeLandmark(points[1], crop),
            NormalizeLandmark(points[61], crop),
            NormalizeLandmark(points[291], crop)
        };
        lock (_gate)
        {
            _orientation = orientation;
            _landmarks = landmarks;
            _previewJpeg = previewJpeg;
            _hasFace = true;
            _status = $"MediaPipe Tasks active ({resolution}, {points.Length} points)";
        }
    }

    private static Vector2 NormalizeLandmark(Point2f point, Rect crop) =>
        new(1f - (point.X - crop.X) / MathF.Max(crop.Width, 1f),
            (point.Y - crop.Y) / MathF.Max(crop.Height, 1f));

    private void SetFaceMissing(string message, byte[] previewJpeg)
    {
        lock (_gate)
        {
            _hasFace = false;
            _previewJpeg = previewJpeg;
            _status = message;
        }
    }

    private void SetStatus(string message)
    {
        lock (_gate)
        {
            _status = message;
            _hasFace = false;
            _previewJpeg = Array.Empty<byte>();
        }
    }

    public void Dispose()
    {
        _cancel?.Cancel();
        _worker?.Wait(TimeSpan.FromSeconds(2));
        _cancel?.Dispose();
    }
}
