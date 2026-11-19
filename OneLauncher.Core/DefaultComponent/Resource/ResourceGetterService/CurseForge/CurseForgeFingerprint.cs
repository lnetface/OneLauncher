namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.CurseForge;

/// <summary>
/// CurseForge 文件指纹：过滤 TAB、LF、CR、空格后的 Murmur2（seed = 1），与 Prism Launcher、packwiz 的实现一致。
/// 指纹只用于在 CurseForge 数据库中定位文件，不是完整性校验。
/// </summary>
internal static class CurseForgeFingerprint
{
    private const uint M = 0x5bd1e995;
    private const int R = 24;
    private const uint Seed = 1;
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// 流式计算文件指纹。算法需要先知道过滤后的长度，因此文件会被读取两遍。
    /// </summary>
    internal static async Task<uint> ComputeAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var buffer = new byte[BufferSize];

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        // 第一遍：过滤后的字节数（协议按 uint32 截断）。
        uint length = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                if (!IsWhitespace(buffer[i]))
                    length = unchecked(length + 1);
            }
        }

        // 第二遍：按 4 字节小端分组做 Murmur2。
        stream.Seek(0, SeekOrigin.Begin);
        uint h = Seed ^ length;
        uint block = 0;
        int blockLength = 0;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                byte b = buffer[i];
                if (IsWhitespace(b))
                    continue;

                block |= (uint)b << (8 * blockLength);
                if (++blockLength < 4)
                    continue;

                unchecked
                {
                    uint k = block * M;
                    k ^= k >> R;
                    k *= M;
                    h *= M;
                    h ^= k;
                }
                block = 0;
                blockLength = 0;
            }
        }

        unchecked
        {
            // 尾部不足 4 字节：block 已按小端存放，等价于依次异或 b2<<16、b1<<8、b0。
            if (blockLength > 0)
            {
                h ^= block;
                h *= M;
            }

            h ^= h >> 13;
            h *= M;
            h ^= h >> 15;
        }
        return h;
    }

    private static bool IsWhitespace(byte b) => b is 0x09 or 0x0A or 0x0D or 0x20;
}
