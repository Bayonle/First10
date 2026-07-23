using System.Text.RegularExpressions;
using First10.Infrastructure.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace First10.Tests;

/// <summary>
/// The §7.1 ≥98% benchmark harness for the labelled 50-image pilot test set (an external
/// dependency — FRSC-corridor crash photos: motion blur, night shots, partial faces, okada
/// helmets). Drop labelled images into tests/blur-testset/ named "&lt;anything&gt;_&lt;N&gt;-faces.jpg"
/// (N = ground-truth face count); the benchmark asserts the gate blurs every labelled face
/// (detected+low-confidence regions ≥ N — over-blurring passes, a missed face fails).
/// Until the set lands the benchmark reports "no test set" and does not gate the build.
/// </summary>
public class BlurBenchmarkTests(ITestOutputHelper output)
{
    private static readonly Regex Label = new(@"_(\d+)-faces\.(jpe?g|png)$", RegexOptions.IgnoreCase);

    [Fact]
    public async Task Labelled_test_set_pass_rate_is_at_least_98_percent()
    {
        var setDir = Path.Combine(BlurGateTests.FindRepoRoot(), "tests", "blur-testset");
        var labelled = Directory.Exists(setDir)
            ? Directory.EnumerateFiles(setDir).Where(f => Label.IsMatch(f)).ToList()
            : [];

        if (labelled.Count == 0)
        {
            output.WriteLine("No labelled test set at tests/blur-testset/ — benchmark not run. " +
                             "This gate MUST pass on the 50-image set before soft launch (G3).");
            return;
        }

        using var blurrer = new UltraFaceBlurrer(
            new BlurOptions
            {
                ModelPath = Path.Combine(BlurGateTests.FindRepoRoot(),
                    "src", "First10.Infrastructure", "Media", "Models", "ultraface-RFB-640.onnx"),
            },
            NullLogger<UltraFaceBlurrer>.Instance);

        int passed = 0;
        var failures = new List<string>();
        foreach (var file in labelled)
        {
            var expected = int.Parse(Label.Match(file).Groups[1].Value);
            await using var stream = File.OpenRead(file);
            var result = await blurrer.BlurAsync(stream, default);

            // Full-frame fallback blurs everything, so it always covers the labelled faces.
            var covered = result.Fallback == First10.Domain.Abstractions.BlurFallback.FullFrame
                          || result.FacesDetected + result.LowConfidenceRegions >= expected;
            if (covered) passed++;
            else failures.Add($"{Path.GetFileName(file)}: expected {expected}, covered {result.FacesDetected}+{result.LowConfidenceRegions}");
        }

        var rate = (double)passed / labelled.Count;
        output.WriteLine($"Blur benchmark: {passed}/{labelled.Count} = {rate:P1}");
        foreach (var f in failures) output.WriteLine($"  MISS {f}");

        Assert.True(rate >= 0.98, $"§7.1 requires ≥98%; measured {rate:P1} on {labelled.Count} images");
    }
}
