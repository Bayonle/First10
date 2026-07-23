using System.Diagnostics;
using First10.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace First10.Infrastructure.Media;

public sealed class BlurOptions
{
    /// <summary>Detections at or above this score are faces. UltraFace scores are well-calibrated; 0.7 is the common operating point.</summary>
    public double ConfidentThreshold { get; set; } = 0.7;

    /// <summary>Detections in [MaybeThreshold, ConfidentThreshold) are maybe-faces: blurred anyway, with a larger region.</summary>
    public double MaybeThreshold { get; set; } = 0.35;

    /// <summary>Box padding for confident faces — covers hair, helmet rims, jawlines.</summary>
    public double PaddingFraction { get; set; } = 0.35;

    /// <summary>Box padding for maybe-faces — deliberately generous (never ship a maybe-face).</summary>
    public double MaybePaddingFraction { get; set; } = 0.7;

    /// <summary>Override path to the ONNX model; default resolves beside the binaries.</summary>
    public string? ModelPath { get; set; }
}

/// <summary>
/// The D-009 blur gate: UltraFace RFB-320 face detection via ONNX Runtime, fully in-process,
/// then irreversible pixelate+blur of every detected region. Conservative by construction:
/// low-confidence detections get an enlarged blur, and if the detector cannot run at all on a
/// decodable image, the whole frame is blurred and flagged — the gate never returns unexamined
/// pixels. Undecodable bytes throw (refused upstream, nothing persisted).
/// </summary>
public sealed class UltraFaceBlurrer : IFaceBlurrer, IDisposable
{
    private readonly BlurOptions _options;
    private readonly ILogger<UltraFaceBlurrer> _logger;
    private readonly InferenceSession? _session;
    private readonly string? _inputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;

    public UltraFaceBlurrer(BlurOptions options, ILogger<UltraFaceBlurrer> logger)
    {
        _options = options;
        _logger = logger;

        // RFB-640 default: markedly better recall on small faces (group scenes) than
        // RFB-320 for ~4x the (still sub-second) inference cost.
        var modelPath = options.ModelPath
            ?? Path.Combine(AppContext.BaseDirectory, "Media", "Models", "ultraface-RFB-640.onnx");
        if (File.Exists(modelPath))
        {
            _session = new InferenceSession(modelPath);
            _inputName = _session.InputMetadata.Keys.First();
            var dims = _session.InputMetadata[_inputName].Dimensions; // [1, 3, H, W]
            _inputHeight = dims[2];
            _inputWidth = dims[3];
        }
        else
        {
            // Missing model is a deployment fault, not a privacy hole: every image goes
            // full-frame blur until it's fixed, and the audit rows make the fault visible.
            _logger.LogError("UltraFace model not found at {Path} — ALL images will be full-frame blurred", modelPath);
        }
    }

    public Task<BlurResult> BlurAsync(Stream image, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        Image<Rgb24> frame;
        try
        {
            frame = Image.Load<Rgb24>(image);
        }
        catch (Exception e) when (e is InvalidImageContentException or UnknownImageFormatException or NotSupportedException)
        {
            throw new NotSupportedException("Image could not be decoded, so it cannot be blurred — refusing it.", e);
        }

        using (frame)
        {
            int faces = 0, maybes = 0;
            double? minConfidence = null;
            var fallback = BlurFallback.None;

            if (_session is null)
            {
                FullFrameBlur(frame);
                fallback = BlurFallback.FullFrame;
            }
            else
            {
                try
                {
                    var detections = Detect(frame);
                    foreach (var d in detections)
                    {
                        var confident = d.Score >= _options.ConfidentThreshold;
                        if (confident) faces++; else maybes++;
                        minConfidence = Math.Min(minConfidence ?? d.Score, d.Score);

                        var padding = confident ? _options.PaddingFraction : _options.MaybePaddingFraction;
                        BlurRegion(frame, d, padding);
                    }

                    if (maybes > 0) fallback = BlurFallback.ExpandedRegions;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Face detection failed — downgrading to full-frame blur");
                    FullFrameBlur(frame);
                    fallback = BlurFallback.FullFrame;
                }
            }

            using var output = new MemoryStream();
            frame.SaveAsJpeg(output);
            sw.Stop();

            return Task.FromResult(new BlurResult(
                output.ToArray(), "image/jpeg", faces, maybes, minConfidence, fallback, sw.ElapsedMilliseconds));
        }
    }

    private readonly record struct Detection(float X1, float Y1, float X2, float Y2, float Score);

    private List<Detection> Detect(Image<Rgb24> frame)
    {
        // UltraFace preprocessing: resize to model input, (pixel - 127) / 128, RGB, NCHW.
        var tensor = new DenseTensor<float>([1, 3, _inputHeight, _inputWidth]);
        using (var resized = frame.Clone(x => x.Resize(_inputWidth, _inputHeight)))
        {
            resized.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < _inputHeight; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < _inputWidth; x++)
                    {
                        tensor[0, 0, y, x] = (row[x].R - 127f) / 128f;
                        tensor[0, 1, y, x] = (row[x].G - 127f) / 128f;
                        tensor[0, 2, y, x] = (row[x].B - 127f) / 128f;
                    }
                }
            });
        }

        using var results = _session!.Run(
            [NamedOnnxValue.CreateFromTensor(_inputName!, tensor)]);
        // Outputs: scores [1, N, 2] (background, face) and boxes [1, N, 4] (normalized x1,y1,x2,y2).
        var all = results.ToDictionary(r => r.Name, r => r.AsTensor<float>());
        var scores = all.Values.First(t => t.Dimensions[^1] == 2);
        var boxes = all.Values.First(t => t.Dimensions[^1] == 4);

        var candidates = new List<Detection>();
        var count = scores.Dimensions[1];
        for (var i = 0; i < count; i++)
        {
            var score = scores[0, i, 1];
            if (score < _options.MaybeThreshold) continue;
            candidates.Add(new Detection(
                boxes[0, i, 0] * frame.Width,
                boxes[0, i, 1] * frame.Height,
                boxes[0, i, 2] * frame.Width,
                boxes[0, i, 3] * frame.Height,
                score));
        }

        return NonMaxSuppression(candidates, iouThreshold: 0.5f);
    }

    private static List<Detection> NonMaxSuppression(List<Detection> candidates, float iouThreshold)
    {
        var kept = new List<Detection>();
        foreach (var c in candidates.OrderByDescending(c => c.Score))
        {
            if (kept.All(k => Iou(k, c) < iouThreshold)) kept.Add(c);
        }
        return kept;
    }

    private static float Iou(Detection a, Detection b)
    {
        var ix = Math.Max(0, Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1));
        var iy = Math.Max(0, Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1));
        var intersection = ix * iy;
        var union = (a.X2 - a.X1) * (a.Y2 - a.Y1) + (b.X2 - b.X1) * (b.Y2 - b.Y1) - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private static void BlurRegion(Image<Rgb24> frame, Detection d, double paddingFraction)
    {
        var padX = (d.X2 - d.X1) * (float)paddingFraction;
        var padY = (d.Y2 - d.Y1) * (float)paddingFraction;
        var x = (int)Math.Clamp(d.X1 - padX, 0, frame.Width - 1);
        var y = (int)Math.Clamp(d.Y1 - padY, 0, frame.Height - 1);
        var w = (int)Math.Clamp(d.X2 + padX, 1, frame.Width) - x;
        var h = (int)Math.Clamp(d.Y2 + padY, 1, frame.Height) - y;
        if (w < 2 || h < 2) return;

        var rect = new Rectangle(x, y, w, h);
        // Pixelate destroys identity irreversibly; the Gaussian pass removes the hard
        // block edges so scene context around the face stays readable in the console.
        var blockSize = Math.Max(10, Math.Min(w, h) / 5);
        frame.Mutate(ctx => ctx.Pixelate(blockSize, rect).GaussianBlur(6f, rect));
    }

    private static void FullFrameBlur(Image<Rgb24> frame)
    {
        var blockSize = Math.Max(16, Math.Min(frame.Width, frame.Height) / 24);
        frame.Mutate(ctx => ctx.Pixelate(blockSize).GaussianBlur(8f));
    }

    public void Dispose() => _session?.Dispose();
}
