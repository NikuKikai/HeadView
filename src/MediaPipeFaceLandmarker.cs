using System;
using System.IO;
using System.Runtime.InteropServices;
using Godot;
using OpenCvSharp;

namespace HeadView;

public sealed class MediaPipeFaceLandmarker : IDisposable
{
    private const string Library = "libmediapipe.dll";
    private const int SrgbFormat = 1;
    private const int VideoMode = 2;
    private readonly Mat _rgb = new();
    private IntPtr _handle;

    static MediaPipeFaceLandmarker()
    {
        NativeLibrary.SetDllImportResolver(typeof(MediaPipeFaceLandmarker).Assembly,
            (name, assembly, _) =>
            {
                if (!string.Equals(name, Library, StringComparison.OrdinalIgnoreCase))
                    return IntPtr.Zero;

                string? assemblyDirectory = Path.GetDirectoryName(assembly.Location);
                if (!string.IsNullOrEmpty(assemblyDirectory))
                {
                    string nextToAssembly = Path.Combine(assemblyDirectory, Library);
                    if (File.Exists(nextToAssembly))
                        return NativeLibrary.Load(nextToAssembly);
                }

                string projectCopy = ProjectSettings.GlobalizePath("res://native/libmediapipe.dll");
                if (File.Exists(projectCopy))
                    return NativeLibrary.Load(projectCopy);

                throw new DllNotFoundException(
                    $"{Library} was not found beside {assembly.Location} or at {projectCopy}.");
            });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BaseOptions
    {
        public IntPtr ModelAssetBuffer;
        public uint ModelAssetBufferCount;
        public IntPtr ModelAssetPath;
        public int FileDescriptor;
        public int Delegate;
        public int HostEnvironment;
        public int HostSystem;
        public IntPtr HostVersion;
        public IntPtr CaBundlePath;
        public IntPtr AppId;
        public IntPtr AppVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FaceLandmarkerOptions
    {
        public BaseOptions BaseOptions;
        public int RunningMode;
        public int NumFaces;
        public float MinFaceDetectionConfidence;
        public float MinFacePresenceConfidence;
        public float MinTrackingConfidence;
        [MarshalAs(UnmanagedType.I1)] public bool OutputFaceBlendshapes;
        [MarshalAs(UnmanagedType.I1)] public bool OutputFacialTransformationMatrixes;
        public IntPtr ResultCallback;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FaceLandmarkerResult
    {
        public IntPtr FaceLandmarks;
        public uint FaceLandmarksCount;
        public IntPtr FaceBlendshapes;
        public uint FaceBlendshapesCount;
        public IntPtr FacialTransformationMatrixes;
        public uint FacialTransformationMatrixesCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NormalizedLandmarks
    {
        public IntPtr Landmarks;
        public uint Count;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NormalizedLandmark
    {
        public float X;
        public float Y;
        public float Z;
        [MarshalAs(UnmanagedType.I1)] public bool HasVisibility;
        public float Visibility;
        [MarshalAs(UnmanagedType.I1)] public bool HasPresence;
        public float Presence;
        public IntPtr Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Matrix
    {
        public uint Rows;
        public uint Columns;
        public IntPtr Data;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int MpFaceLandmarkerCreate(ref FaceLandmarkerOptions options,
        out IntPtr handle, out IntPtr error);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int MpFaceLandmarkerDetectForVideo(IntPtr handle, IntPtr image,
        IntPtr imageProcessingOptions, long timestampMs, out FaceLandmarkerResult result,
        out IntPtr error);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void MpFaceLandmarkerCloseResult(ref FaceLandmarkerResult result);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int MpFaceLandmarkerClose(IntPtr handle, out IntPtr error);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int MpImageCreateFromUint8Data(int format, int width, int height,
        byte[] data, int dataCount, out IntPtr image, out IntPtr error);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void MpImageFree(IntPtr image);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void MpErrorFree(IntPtr error);

    public MediaPipeFaceLandmarker(string modelPath)
    {
        IntPtr path = Marshal.StringToCoTaskMemUTF8(modelPath);
        try
        {
            var options = new FaceLandmarkerOptions
            {
                BaseOptions = new BaseOptions { ModelAssetPath = path, FileDescriptor = -1 },
                RunningMode = VideoMode,
                NumFaces = 1,
                MinFaceDetectionConfidence = 0.5f,
                MinFacePresenceConfidence = 0.5f,
                MinTrackingConfidence = 0.5f,
                OutputFacialTransformationMatrixes = true
            };
            Check(MpFaceLandmarkerCreate(ref options, out _handle, out IntPtr error), error);
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    public bool TryDetect(Mat frame, long timestampMs, out Vector3 orientation,
        out Point2f[] points)
    {
        orientation = Vector3.Zero;
        points = Array.Empty<Point2f>();
        Cv2.CvtColor(frame, _rgb, ColorConversionCodes.BGR2RGB);
        int byteCount = checked((int)(_rgb.Total() * _rgb.ElemSize()));
        byte[] pixels = new byte[byteCount];
        Marshal.Copy(_rgb.Data, pixels, 0, byteCount);
        Check(MpImageCreateFromUint8Data(SrgbFormat, frame.Width, frame.Height,
            pixels, byteCount, out IntPtr image, out IntPtr imageError), imageError);
        try
        {
            Check(MpFaceLandmarkerDetectForVideo(_handle, image, IntPtr.Zero,
                timestampMs, out FaceLandmarkerResult result, out IntPtr error), error);
            try
            {
                if (result.FaceLandmarksCount == 0 || result.FacialTransformationMatrixesCount == 0)
                    return false;
                var landmarkSet = Marshal.PtrToStructure<NormalizedLandmarks>(result.FaceLandmarks);
                var matrix = Marshal.PtrToStructure<Matrix>(result.FacialTransformationMatrixes);
                if (landmarkSet.Count < 468 || matrix.Rows != 4 || matrix.Columns != 4 ||
                    landmarkSet.Landmarks == IntPtr.Zero || matrix.Data == IntPtr.Zero)
                    return false;

                points = new Point2f[landmarkSet.Count];
                int pointStride = Marshal.SizeOf<NormalizedLandmark>();
                for (int i = 0; i < points.Length; i++)
                {
                    var point = Marshal.PtrToStructure<NormalizedLandmark>(
                        IntPtr.Add(landmarkSet.Landmarks, i * pointStride));
                    points[i] = new Point2f(point.X * frame.Width, point.Y * frame.Height);
                }

                float[] pose = new float[16];
                Marshal.Copy(matrix.Data, pose, 0, pose.Length);
                float yaw = -Mathf.RadToDeg(Mathf.Atan2(pose[8], pose[10]));
                float pitch = Mathf.RadToDeg(Mathf.Atan2(pose[9], pose[10]));
                float roll = Mathf.RadToDeg(Mathf.Atan2(pose[1], pose[5]));
                if (!float.IsFinite(yaw) || !float.IsFinite(pitch) || !float.IsFinite(roll))
                    return false;
                orientation = new Vector3(yaw, pitch, roll);
                return true;
            }
            finally
            {
                MpFaceLandmarkerCloseResult(ref result);
            }
        }
        finally
        {
            MpImageFree(image);
        }
    }

    private static void Check(int status, IntPtr error)
    {
        string message = error == IntPtr.Zero ? "Unknown MediaPipe error" :
            Marshal.PtrToStringUTF8(error) ?? "Unknown MediaPipe error";
        if (error != IntPtr.Zero) MpErrorFree(error);
        if (status != 0) throw new InvalidOperationException($"MediaPipe error {status}: {message}");
    }

    public void Dispose()
    {
        _rgb.Dispose();
        if (_handle == IntPtr.Zero) return;
        int status = MpFaceLandmarkerClose(_handle, out IntPtr error);
        _handle = IntPtr.Zero;
        Check(status, error);
    }
}
