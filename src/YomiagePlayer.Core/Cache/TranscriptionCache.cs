using System.Text.Json;
using YomiagePlayer.Core.Models;

namespace YomiagePlayer.Core.Cache;

/// <summary>
/// 文字起こし結果のJSONキャッシュ。ファイル名は {hashKey}-{model}.json で、
/// モデルを切り替えても既存キャッシュを壊さない。
/// 書き込みは .tmp → rename の原子的置換で、解析中のクラッシュで
/// 破損ファイルが残らないようにする。
/// </summary>
public class TranscriptionCache(string cacheDir)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private string PathFor(string hashKey, string model)
        => Path.Combine(cacheDir, $"{hashKey}-{model}.json");

    public void Save(TranscriptionResult result)
    {
        Directory.CreateDirectory(cacheDir);
        var final = PathFor(result.HashKey, result.Model);
        var tmp = final + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(result, JsonOpts));
        File.Move(tmp, final, overwrite: true);
    }

    public bool TryLoad(string hashKey, string model, out TranscriptionResult? result)
    {
        result = null;
        var path = PathFor(hashKey, model);
        if (!File.Exists(path)) return false;
        try
        {
            result = JsonSerializer.Deserialize<TranscriptionResult>(File.ReadAllText(path), JsonOpts);
            if (result is null || result.Version != 1)
            {
                result = null;
                return false;
            }
            return true;
        }
        catch (JsonException)
        {
            result = null;
            return false;
        }
    }

    public void Delete(string hashKey, string model)
    {
        var path = PathFor(hashKey, model);
        if (File.Exists(path)) File.Delete(path);
    }
}
