using System.Security.Cryptography;

namespace YomiagePlayer.Core.Cache;

/// <summary>
/// ファイル内容から高速な合成キーを算出する。
/// key = SHA256(fileSize(8byte LE) + 先頭1MB + 末尾1MB)。
/// フルハッシュと違い巨大ファイルでも数十msで済み、
/// リネーム・移動しても同一内容なら同じキーになる。
/// </summary>
public static class ContentHasher
{
    private const int ChunkSize = 1024 * 1024;

    public static string ComputeKey(string filePath)
    {
        // 再生中(LibVLCが掴んでいる)ファイルも読めるよう共有モードを緩くする
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();

        var sizeBytes = BitConverter.GetBytes(fs.Length);
        sha.TransformBlock(sizeBytes, 0, sizeBytes.Length, null, 0);

        var buffer = new byte[ChunkSize];
        int headRead = ReadFully(fs, buffer);
        sha.TransformBlock(buffer, 0, headRead, null, 0);

        if (fs.Length > ChunkSize)
        {
            // 末尾1MB(ただしheadと重複しない範囲)
            fs.Seek(Math.Max(fs.Length - ChunkSize, ChunkSize), SeekOrigin.Begin);
            int tailRead = ReadFully(fs, buffer);
            sha.TransformBlock(buffer, 0, tailRead, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(sha.Hash!);
    }

    private static int ReadFully(Stream s, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = s.Read(buffer, total, buffer.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
