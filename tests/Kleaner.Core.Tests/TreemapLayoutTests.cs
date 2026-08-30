using Kleaner.Analysis;

namespace Kleaner.Core.Tests;

/// <summary>Squarified treemap 布局的几何不变量：面积守恒、边界紧凑、无重叠。</summary>
public class TreemapLayoutTests
{
    [Fact]
    public void Empty_and_invalid_inputs_yield_no_rects()
    {
        Assert.Empty(TreemapLayout.Squarify(Array.Empty<(string, long)>(), 800, 600));
        Assert.Empty(TreemapLayout.Squarify(new[] { ("a", 0L), ("b", -5L) }, 800, 600));
        Assert.Empty(TreemapLayout.Squarify(new[] { ("a", 10L) }, 0, 600));
        Assert.Empty(TreemapLayout.Squarify(new[] { ("a", 10L) }, 800, -1));
    }

    [Fact]
    public void Single_item_fills_whole_area()
    {
        var rects = TreemapLayout.Squarify(new[] { ("only", 100L) }, 400, 300);
        var r = Assert.Single(rects);
        Assert.Equal(400 * 300, r.Width * r.Height, 5);
    }

    [Fact]
    public void Total_area_is_conserved()
    {
        var items = Enumerable.Range(1, 40).Select(i => ($"item-{i}", (long)i * i * 1024)).ToList();
        var rects = TreemapLayout.Squarify(items, 800, 600);
        Assert.Equal(items.Count, rects.Count);
        var total = rects.Sum(r => r.Width * r.Height);
        Assert.Equal(800 * 600, total, 2);
    }

    [Fact]
    public void Rects_stay_inside_bounds_and_do_not_overlap()
    {
        var items = Enumerable.Range(1, 25).Select(i => ($"item-{i}", (long)(i * 997) % 5000 + 1)).ToList();
        var rects = TreemapLayout.Squarify(items, 640, 480).ToList();
        foreach (var r in rects)
        {
            Assert.True(r.X >= -0.001 && r.Y >= -0.001, $"{r.Path} 越界");
            Assert.True(r.X + r.Width <= 640.001 && r.Y + r.Height <= 480.001, $"{r.Path} 越界");
        }
        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                var a = rects[i];
                var b = rects[j];
                var overlapX = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
                var overlapY = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
                Assert.True(overlapX <= 0.001 || overlapY <= 0.001, $"{a.Path} 与 {b.Path} 重叠");
            }
        }
    }

    [Fact]
    public void Uniform_items_get_reasonable_aspect_ratios()
    {
        // 等面积项的 squarified 布局长宽比应明显优于逐条切分（保证 < 8）
        var items = Enumerable.Range(1, 16).Select(i => ($"u{i}", 1000L)).ToList();
        var rects = TreemapLayout.Squarify(items, 400, 400);
        Assert.All(rects, r =>
        {
            var ratio = Math.Max(r.Width / r.Height, r.Height / r.Width);
            Assert.True(ratio < 8, $"{r.Path} 长宽比过大：{ratio:F1}");
        });
    }

    [Fact]
    public void Larger_items_get_larger_areas()
    {
        var items = new List<(string, long)> { ("small", 1), ("medium", 10), ("large", 100) };
        var rects = TreemapLayout.Squarify(items, 500, 500).ToDictionary(r => r.Path);
        Assert.True(rects["large"].Width * rects["large"].Height > rects["medium"].Width * rects["medium"].Height);
        Assert.True(rects["medium"].Width * rects["medium"].Height > rects["small"].Width * rects["small"].Height);
    }
}
