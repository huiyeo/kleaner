using System.Security.Cryptography;

namespace Kleaner.Analysis;

public sealed record DuplicateGroup(string Hash, long SizeBytes, IReadOnlyList<string> Files);

/// <summary>重复文件查找：按内容（SHA256）而非文件名判定。两级预筛（大小分组 → 首块哈希）避免全量哈希浪费。
/// 调用方必须保证每组至少保留一份，删除走隔离区。</summary>
public static class DuplicateFinder
{
    public const int PartialHashBytes = 64 * 1024;

    public static IReadOnlyList<DuplicateGroup> Find(
        string root, long minBytesPerFile, CancellationToken token = default)
    {
        // 第一级：按大小分组，唯一大小不可能重复
        var bySize = new Dictionary<long, List<string>>();
        foreach (var path in FileWalker.EnumerateFiles(root, token))
        {
            try
            {
                var len = new FileInfo(path).Length;
                if (len < minBytesPerFile)
                    continue;
                if (!bySize.TryGetValue(len, out var list))
                    bySize[len] = list = new List<string>();
                list.Add(path);
            }
            catch
            {
            }
        }

        var result = new List<DuplicateGroup>();
        foreach (var (size, sameSize) in bySize.Where(kv => kv.Value.Count > 1))
        {
            token.ThrowIfCancellationRequested();

            // 第二级：首块哈希预筛
            var byPartial = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var path in sameSize)
            {
                var hash = HashFile(path, PartialHashBytes, token);
                if (hash is null)
                    continue;
                if (!byPartial.TryGetValue(hash, out var list))
                    byPartial[hash] = list = new List<string>();
                list.Add(path);
            }

            foreach (var (_, candidates) in byPartial.Where(kv => kv.Value.Count > 1))
            {
                // 第三级：全量哈希确证
                var byFull = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var path in candidates)
                {
                    var hash = HashFile(path, null, token);
                    if (hash is null)
                        continue;
                    if (!byFull.TryGetValue(hash, out var list))
                        byFull[hash] = list = new List<string>();
                    list.Add(path);
                }

                foreach (var (hash, files) in byFull.Where(kv => kv.Value.Count > 1))
                    result.Add(new DuplicateGroup(hash, size, files));
            }
        }

        return result.OrderByDescending(g => g.SizeBytes * (g.Files.Count - 1)).ToList();
    }

    private static string? HashFile(string path, int? maxBytes, CancellationToken token)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            if (maxBytes is null || stream.Length <= maxBytes.Value)
                return Convert.ToHexString(sha.ComputeHash(stream));
            var buffer = new byte[maxBytes.Value];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0)
                    break;
                read += n;
            }
            return Convert.ToHexString(sha.ComputeHash(buffer, 0, read));
        }
        catch
        {
            return null;
        }
    }
}
