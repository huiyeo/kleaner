namespace Kleaner.Analysis;

public sealed record TreemapRect(string Path, long SizeBytes, double X, double Y, double Width, double Height);

/// <summary>
/// Squarified treemap 布局（Bruls/Huizing/van Wijk）：把加权面积切分为接近正方形的矩形。
/// 行沿剩余区域的较长边铺设，厚度伸向较短边。纯函数：非正面积项被忽略。
/// </summary>
public static class TreemapLayout
{
    public static IReadOnlyList<TreemapRect> Squarify(
        IEnumerable<(string Path, long SizeBytes)> items, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return Array.Empty<TreemapRect>();

        var valid = items
            .Where(i => i.SizeBytes > 0)
            .OrderByDescending(i => i.SizeBytes)
            .ToList();
        var totalSize = valid.Sum(i => i.SizeBytes);
        if (valid.Count == 0 || totalSize == 0)
            return Array.Empty<TreemapRect>();

        var nodes = valid
            .Select(i => (i.Path, i.SizeBytes, Area: width * height * ((double)i.SizeBytes / totalSize)))
            .ToList();

        var result = new List<TreemapRect>(nodes.Count);
        var x = 0.0;
        var y = 0.0;
        var w = width;
        var h = height;
        var row = new List<(string Path, long Size, double Area)>();
        double rowArea = 0;

        foreach (var node in nodes)
        {
            // 试算把 node 加入当前行后的最差长宽比，变差则先排空当前行
            var along = Math.Max(w, h);
            if (row.Count > 0 && Worst(row, rowArea, along) < WorstWith(row, node, rowArea + node.Area, along))
            {
                (x, y, w, h) = LayoutRow(result, row, rowArea, x, y, w, h);
                row = new List<(string, long, double)>();
                rowArea = 0;
            }
            row.Add(node);
            rowArea += node.Area;
        }
        if (row.Count > 0)
            LayoutRow(result, row, rowArea, x, y, w, h);

        return result;
    }

    /// <summary>行厚 t = rowArea/along；项 i 跨度 span = a_i/t。返回行内最差长宽比。</summary>
    private static double Worst(IReadOnlyList<(string Path, long Size, double Area)> row, double rowArea, double along)
    {
        var t = rowArea / along;
        var worst = 0.0;
        foreach (var item in row)
        {
            var span = item.Area / t;
            var ratio = Math.Max(t / span, span / t);
            if (ratio > worst)
                worst = ratio;
        }
        return worst;
    }

    private static double WorstWith(
        IReadOnlyList<(string Path, long Size, double Area)> row,
        (string Path, long Size, double Area) node,
        double newArea,
        double along)
    {
        var merged = new List<(string, long, double)>(row) { node };
        return Worst(merged, newArea, along);
    }

    /// <summary>把当前行贴到剩余区域较长边的整条边上，返回剩余区域。</summary>
    private static (double X, double Y, double W, double H) LayoutRow(
        IList<TreemapRect> result,
        List<(string Path, long Size, double Area)> row,
        double rowArea,
        double x, double y, double w, double h)
    {
        var horizontal = w >= h; // 行沿宽度方向铺设，厚度向下
        var along = horizontal ? w : h;
        var thickness = Math.Min(rowArea / along, horizontal ? h : w);
        var offset = 0.0;
        for (var i = 0; i < row.Count; i++)
        {
            var item = row[i];
            var span = item.Area / rowArea * along;
            if (i == row.Count - 1)
                span = along - offset; // 消除浮点累加误差，保证恰好铺满
            result.Add(horizontal
                ? new TreemapRect(item.Path, item.Size, x + offset, y, span, thickness)
                : new TreemapRect(item.Path, item.Size, x, y + offset, thickness, span));
            offset += span;
        }
        return horizontal
            ? (x, y + thickness, w, h - thickness)
            : (x + thickness, y, w - thickness, h);
    }
}
