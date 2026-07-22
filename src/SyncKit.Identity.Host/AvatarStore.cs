namespace SyncKit.Identity.Host;

public static class AvatarStore {
    public const long MaxBytes = 2 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedTypes = new() {
        ["image/png"] = "png",
        ["image/jpeg"] = "jpg",
        ["image/webp"] = "webp",
    };

    public static async Task<string?> SaveAsync(string dir, Guid userId, Stream content, string contentType, CancellationToken ct) {
        if (!AllowedTypes.TryGetValue(contentType, out var ext)) return null;
        if (content.CanSeek && content.Length > MaxBytes) return null;

        Directory.CreateDirectory(dir);
        foreach (var existingExt in AllowedTypes.Values) {
            var existingPath = Path.Combine(dir, $"{userId}.{existingExt}");
            if (File.Exists(existingPath)) File.Delete(existingPath);
        }

        var path = Path.Combine(dir, $"{userId}.{ext}");
        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream, ct);
        if (fileStream.Length > MaxBytes) {
            fileStream.Close();
            File.Delete(path);
            return null;
        }

        return $"/avatars/{userId}";
    }

    public static bool TryGetPath(string dir, Guid userId, out string path, out string contentType) {
        foreach (var (mime, ext) in AllowedTypes) {
            var candidate = Path.Combine(dir, $"{userId}.{ext}");
            if (File.Exists(candidate)) {
                path = candidate;
                contentType = mime;
                return true;
            }
        }
        path = "";
        contentType = "";
        return false;
    }
}
