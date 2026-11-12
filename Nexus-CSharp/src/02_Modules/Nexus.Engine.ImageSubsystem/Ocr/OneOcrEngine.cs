using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Nexus.Engine.ImageSubsystem.Ocr;

/// <summary>
/// Native interop wrapper for Microsoft OneOCR (Snipping Tool / Windows Photos).
/// Single, dedicated OCR engine for the NEXUS Image Tool.
/// Produces true word-level 4-point quadrilateral geometries.
/// </summary>
public sealed class OneOcrEngine : IDisposable
{
    private static readonly object _syncRoot = new();

    #region Win32 Native Interop
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);

    [StructLayout(LayoutKind.Sequential)]
    private struct Img
    {
        public int t;        // 3 for 32bpp BGRA
        public int col;      // width
        public int row;      // height
        public int _unk;     // 0
        public long step;    // stride
        public IntPtr data_ptr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBoundingBox
    {
        public float x1;
        public float y1;
        public float x2;
        public float y2;
        public float x3;
        public float y3;
        public float x4;
        public float y4;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long CreateOcrInitOptions_t(out long ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long OcrInitOptionsSetUseModelDelayLoad_t(long ctx, byte flag);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long CreateOcrPipeline_t(string modelPath, string key, long ctx, out long pipeline);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long CreateOcrProcessOptions_t(out long opt);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long OcrProcessOptionsSetMaxRecognitionLineCount_t(long opt, long count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long RunOcrPipeline_t(long pipeline, ref Img img, long opt, out long instance);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetOcrLineCount_t(long instance, out long count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetOcrLine_t(long instance, long index, out long line);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetOcrLineContent_t(long line, out IntPtr content);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetOcrLineBoundingBox_t(long line, out IntPtr boundingBoxPtr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetOcrLineWordCount_t(long line, out long count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetOcrWord_t(long line, long index, out long word);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetOcrWordContent_t(long word, out IntPtr content);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetOcrWordBoundingBox_t(long word, out IntPtr boundingBoxPtr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetOcrWordConfidence_t(long word, out float confidence);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetImageAngle_t(long instance, out float angle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long ReleaseOcrInitOptions_t(long ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long ReleaseOcrProcessOptions_t(long opt);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long ReleaseOcrPipeline_t(long pipeline);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long ReleaseOcrResult_t(long instance);
    #endregion

    private IntPtr _hMod = IntPtr.Zero;
    private long _initCtx;
    private long _pipeline;
    private long _processOpt;
    private bool _isDisposed;

    private CreateOcrInitOptions_t? _fnCreateInit;
    private OcrInitOptionsSetUseModelDelayLoad_t? _fnSetDelayLoad;
    private CreateOcrPipeline_t? _fnCreatePipeline;
    private CreateOcrProcessOptions_t? _fnCreateOpt;
    private OcrProcessOptionsSetMaxRecognitionLineCount_t? _fnSetMaxLines;
    private RunOcrPipeline_t? _fnRun;
    private GetOcrLineCount_t? _fnGetLineCount;
    private GetOcrLine_t? _fnGetLine;
    private GetOcrLineContent_t? _fnGetLineContent;
    private GetOcrLineBoundingBox_t? _fnGetLineBBox;
    private GetOcrLineWordCount_t? _fnGetLineWordCount;
    private GetOcrWord_t? _fnGetWord;
    private GetOcrWordContent_t? _fnGetWordContent;
    private GetOcrWordBoundingBox_t? _fnGetWordBBox;
    private GetOcrWordConfidence_t? _fnGetWordConf;
    private GetImageAngle_t? _fnGetAngle;
    private ReleaseOcrInitOptions_t? _fnRelInit;
    private ReleaseOcrProcessOptions_t? _fnRelOpt;
    private ReleaseOcrPipeline_t? _fnRelPipeline;
    private ReleaseOcrResult_t? _fnRelResult;

    private const string DecryptionKey = "kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4";

    public OneOcrEngine()
    {
        Initialize();
    }

    private void Initialize()
    {
        lock (_syncRoot)
        {
            var (dllPath, modelPath) = ResolveAndStageBinaries();

            string dir = Path.GetDirectoryName(dllPath)!;
            SetDllDirectory(dir);

            _hMod = LoadLibrary(dllPath);
            if (_hMod == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new OneOcrNotAvailableException($"Gagal memuat oneocr.dll dari '{dllPath}'. Win32 Error: {err}");
            }

            T Bind<T>(string name) where T : Delegate
            {
                IntPtr p = GetProcAddress(_hMod, name);
                if (p == IntPtr.Zero)
                {
                    throw new OneOcrNotAvailableException($"Export '{name}' tidak ditemukan pada {dllPath}.");
                }
                return Marshal.GetDelegateForFunctionPointer<T>(p);
            }

            _fnCreateInit = Bind<CreateOcrInitOptions_t>("CreateOcrInitOptions");
            _fnSetDelayLoad = Bind<OcrInitOptionsSetUseModelDelayLoad_t>("OcrInitOptionsSetUseModelDelayLoad");
            _fnCreatePipeline = Bind<CreateOcrPipeline_t>("CreateOcrPipeline");
            _fnCreateOpt = Bind<CreateOcrProcessOptions_t>("CreateOcrProcessOptions");
            _fnSetMaxLines = Bind<OcrProcessOptionsSetMaxRecognitionLineCount_t>("OcrProcessOptionsSetMaxRecognitionLineCount");
            _fnRun = Bind<RunOcrPipeline_t>("RunOcrPipeline");
            _fnGetLineCount = Bind<GetOcrLineCount_t>("GetOcrLineCount");
            _fnGetLine = Bind<GetOcrLine_t>("GetOcrLine");
            _fnGetLineContent = Bind<GetOcrLineContent_t>("GetOcrLineContent");
            _fnGetLineBBox = Bind<GetOcrLineBoundingBox_t>("GetOcrLineBoundingBox");
            _fnGetLineWordCount = Bind<GetOcrLineWordCount_t>("GetOcrLineWordCount");
            _fnGetWord = Bind<GetOcrWord_t>("GetOcrWord");
            _fnGetWordContent = Bind<GetOcrWordContent_t>("GetOcrWordContent");
            _fnGetWordBBox = Bind<GetOcrWordBoundingBox_t>("GetOcrWordBoundingBox");
            _fnGetWordConf = Bind<GetOcrWordConfidence_t>("GetOcrWordConfidence");
            _fnGetAngle = Bind<GetImageAngle_t>("GetImageAngle");
            _fnRelInit = Bind<ReleaseOcrInitOptions_t>("ReleaseOcrInitOptions");
            _fnRelOpt = Bind<ReleaseOcrProcessOptions_t>("ReleaseOcrProcessOptions");
            _fnRelPipeline = Bind<ReleaseOcrPipeline_t>("ReleaseOcrPipeline");
            _fnRelResult = Bind<ReleaseOcrResult_t>("ReleaseOcrResult");

            long res = _fnCreateInit(out _initCtx);
            if (res != 0)
                throw new OneOcrNotAvailableException($"CreateOcrInitOptions gagal dengan kode: {res}");

            res = _fnSetDelayLoad(_initCtx, 0);
            if (res != 0)
                Trace.WriteLine($"[OneOcrEngine] OcrInitOptionsSetUseModelDelayLoad status: {res}");

            res = _fnCreatePipeline(modelPath, DecryptionKey, _initCtx, out _pipeline);
            if (res != 0)
                throw new OneOcrNotAvailableException($"CreateOcrPipeline gagal dengan kode: {res}. Model: {modelPath}");

            res = _fnCreateOpt(out _processOpt);
            if (res != 0)
                throw new OneOcrNotAvailableException($"CreateOcrProcessOptions gagal dengan kode: {res}");

            _fnSetMaxLines(_processOpt, 1000);
        }
    }

    /// <summary>
    /// Executes OneOCR on the provided Bitmap and returns full document with lines and word quads.
    /// </summary>
    public OneOcrDocument RunPipeline(Bitmap sourceBitmap)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(sourceBitmap);

        lock (_syncRoot)
        {
            var sw = Stopwatch.StartNew();
            int width = sourceBitmap.Width;
            int height = sourceBitmap.Height;

            // Ensure 32bpp format
            using var bmp32 = (sourceBitmap.PixelFormat == PixelFormat.Format32bppArgb || sourceBitmap.PixelFormat == PixelFormat.Format32bppPArgb)
                ? (Bitmap)sourceBitmap.Clone()
                : ConvertTo32bpp(sourceBitmap);

            var bmpData = bmp32.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            long instance = 0;
            try
            {
                var imgStruct = new Img
                {
                    t = 3,
                    col = width,
                    row = height,
                    _unk = 0,
                    step = bmpData.Stride,
                    data_ptr = bmpData.Scan0
                };

                long res = _fnRun!(_pipeline, ref imgStruct, _processOpt, out instance);
                if (res != 0 || instance == 0)
                {
                    throw new InvalidOperationException($"RunOcrPipeline gagal dengan kode: {res}");
                }

                float angle = 0;
                if (_fnGetAngle != null)
                {
                    _fnGetAngle(instance, out angle);
                }

                _fnGetLineCount!(instance, out long lineCount);
                var lines = new List<OneOcrLine>((int)lineCount);
                var allWords = new List<OneOcrWord>();
                var fullTextBuilder = new StringBuilder();

                int globalWordIdx = 0;

                for (long i = 0; i < lineCount; i++)
                {
                    _fnGetLine!(instance, i, out long lineHandle);
                    if (lineHandle == 0) continue;

                    _fnGetLineContent!(lineHandle, out IntPtr lineContentPtr);
                    string lineText = PtrToUtf8(lineContentPtr);

                    _fnGetLineBBox!(lineHandle, out IntPtr lineBBoxPtr);
                    var lineBox = Marshal.PtrToStructure<NativeBoundingBox>(lineBBoxPtr);
                    var lineQuad = new OcrQuad(lineBox.x1, lineBox.y1, lineBox.x2, lineBox.y2, lineBox.x3, lineBox.y3, lineBox.x4, lineBox.y4);

                    _fnGetLineWordCount!(lineHandle, out long wordCount);
                    var words = new List<OneOcrWord>((int)wordCount);

                    for (long j = 0; j < wordCount; j++)
                    {
                        _fnGetWord!(lineHandle, j, out long wordHandle);
                        if (wordHandle == 0) continue;

                        _fnGetWordContent!(wordHandle, out IntPtr wordContentPtr);
                        string wordText = PtrToUtf8(wordContentPtr);

                        _fnGetWordBBox!(wordHandle, out IntPtr wordBBoxPtr);
                        var wordBox = Marshal.PtrToStructure<NativeBoundingBox>(wordBBoxPtr);
                        var wordQuad = new OcrQuad(wordBox.x1, wordBox.y1, wordBox.x2, wordBox.y2, wordBox.x3, wordBox.y3, wordBox.x4, wordBox.y4);

                        float conf = 1.0f;
                        if (_fnGetWordConf != null)
                        {
                            _fnGetWordConf(wordHandle, out conf);
                        }

                        var word = new OneOcrWord
                        {
                            GlobalIndex = globalWordIdx++,
                            LineIndex = (int)i,
                            WordIndex = (int)j,
                            Text = wordText,
                            Confidence = conf,
                            Quad = wordQuad
                        };

                        words.Add(word);
                        allWords.Add(word);
                    }

                    var line = new OneOcrLine
                    {
                        LineIndex = (int)i,
                        Text = lineText,
                        Quad = lineQuad,
                        Words = words
                    };

                    lines.Add(line);
                    if (fullTextBuilder.Length > 0) fullTextBuilder.AppendLine();
                    fullTextBuilder.Append(lineText);
                }

                sw.Stop();

                return new OneOcrDocument
                {
                    ImageWidth = width,
                    ImageHeight = height,
                    ImageAngle = angle,
                    Lines = lines,
                    AllWords = allWords,
                    FullText = fullTextBuilder.ToString(),
                    InferenceMs = sw.ElapsedMilliseconds
                };
            }
            finally
            {
                bmp32.UnlockBits(bmpData);
                if (instance != 0 && _fnRelResult != null)
                {
                    _fnRelResult(instance);
                }
            }
        }
    }

    private static Bitmap ConvertTo32bpp(Bitmap src)
    {
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.DrawImage(src, 0, 0, src.Width, src.Height);
        return dst;
    }

    private static string PtrToUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return string.Empty;
        int len = 0;
        while (Marshal.ReadByte(ptr, len) != 0) len++;
        if (len == 0) return string.Empty;
        byte[] buf = new byte[len];
        Marshal.Copy(ptr, buf, 0, len);
        return Encoding.UTF8.GetString(buf);
    }

    /// <summary>
    /// Discovers OneOCR binaries from the host Windows installation and stages them to LocalAppData.
    /// This bypasses WindowsApps Win32 Error 5 (Access Denied) when calling LoadLibrary in unpackaged apps.
    /// </summary>
    private static (string DllPath, string ModelPath) ResolveAndStageBinaries()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string stageDir = Path.Combine(localAppData, "Nexus", "OneOcr");
        string stagedDll = Path.Combine(stageDir, "oneocr.dll");
        string stagedModel = Path.Combine(stageDir, "oneocr.onemodel");
        string stagedOnnx = Path.Combine(stageDir, "onnxruntime.dll");

        // 1. Check if already staged and intact
        if (File.Exists(stagedDll) && File.Exists(stagedModel) && File.Exists(stagedOnnx))
        {
            var fiDll = new FileInfo(stagedDll);
            var fiModel = new FileInfo(stagedModel);
            if (fiDll.Length > 10_000_000 && fiModel.Length > 10_000_000)
            {
                return (stagedDll, stagedModel);
            }
        }

        // 2. Check local application directory (for dev/test environments)
        string appBase = AppDomain.CurrentDomain.BaseDirectory;
        string appDll = Path.Combine(appBase, "oneocr.dll");
        string appModel = Path.Combine(appBase, "oneocr.onemodel");
        if (File.Exists(appDll) && File.Exists(appModel))
        {
            return (appDll, appModel);
        }

        // 3. Search WindowsApps packages via Windows Registry (standard user accessible)
        string? foundDll = null;
        string? foundModel = null;
        string? foundOnnx = null;

        var candidateDirs = new List<string>();

        // A. Search CurrentUser AppModel Repository (No elevation required)
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (key != null)
            {
                foreach (var subName in key.GetSubKeyNames())
                {
                    if (subName.StartsWith("Microsoft.ScreenSketch_", StringComparison.OrdinalIgnoreCase) ||
                        subName.StartsWith("Microsoft.Windows.Photos_", StringComparison.OrdinalIgnoreCase))
                    {
                        using var subKey = key.OpenSubKey(subName);
                        var root = subKey?.GetValue("PackageRootFolder") as string;
                        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                        {
                            candidateDirs.Add(Path.Combine(root, "SnippingTool"));
                            candidateDirs.Add(root);
                        }
                    }
                }
            }
        }
        catch (Exception exReg)
        {
            Trace.WriteLine($"[OneOcrEngine] Registry search encountered: {exReg.Message}");
        }

        foreach (var dir in candidateDirs)
        {
            if (!Directory.Exists(dir)) continue;

            string dPath = Path.Combine(dir, "oneocr.dll");
            string mPath = Path.Combine(dir, "oneocr.onemodel");
            string oPath = Path.Combine(dir, "onnxruntime.dll");

            if (File.Exists(dPath) && File.Exists(mPath))
            {
                foundDll = dPath;
                foundModel = mPath;
                foundOnnx = File.Exists(oPath) ? oPath : null;
                break;
            }
        }

        if (foundDll == null || foundModel == null)
        {
            throw new OneOcrNotAvailableException(
                "Microsoft OneOCR tidak ditemukan pada sistem Windows 11 ini.\n" +
                "Pastikan Snipping Tool atau Microsoft Photos telah terpasang.");
        }

        // Stage files to LocalAppData/Nexus/OneOcr/
        try
        {
            if (foundOnnx == null && foundDll != null)
            {
                string dir = Path.GetDirectoryName(foundDll)!;
                string pOnnx = Path.Combine(dir, "onnxruntime.dll");
                if (File.Exists(pOnnx)) foundOnnx = pOnnx;
                else
                {
                    string? parent = Path.GetDirectoryName(dir);
                    if (parent != null)
                    {
                        string ppOnnx = Path.Combine(parent, "onnxruntime.dll");
                        if (File.Exists(ppOnnx)) foundOnnx = ppOnnx;
                    }
                }
            }

            Directory.CreateDirectory(stageDir);
            CopyIfChanged(foundDll!, stagedDll);
            CopyIfChanged(foundModel!, stagedModel);
            if (foundOnnx != null)
            {
                CopyIfChanged(foundOnnx, stagedOnnx);
            }

            return (stagedDll, stagedModel);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OneOcrEngine] Staging to LocalAppData failed: {ex.Message}");
            // Fallback to source paths directly (may fail with Error 5 if unpackaged)
            return (foundDll, foundModel);
        }
    }

    private static void CopyIfChanged(string src, string dst)
    {
        if (File.Exists(dst))
        {
            var fiSrc = new FileInfo(src);
            var fiDst = new FileInfo(dst);
            if (fiSrc.Length == fiDst.Length)
                return;
        }
        File.Copy(src, dst, overwrite: true);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        lock (_syncRoot)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_processOpt != 0 && _fnRelOpt != null)
            {
                _fnRelOpt(_processOpt);
                _processOpt = 0;
            }

            if (_pipeline != 0 && _fnRelPipeline != null)
            {
                _fnRelPipeline(_pipeline);
                _pipeline = 0;
            }

            if (_initCtx != 0 && _fnRelInit != null)
            {
                _fnRelInit(_initCtx);
                _initCtx = 0;
            }

            if (_hMod != IntPtr.Zero)
            {
                FreeLibrary(_hMod);
                _hMod = IntPtr.Zero;
            }
        }
    }
}
