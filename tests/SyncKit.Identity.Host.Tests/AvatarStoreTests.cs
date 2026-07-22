using SyncKit.Identity.Host;
using Xunit;

namespace SyncKit.Identity.Host.Tests;

public class AvatarStoreTests {
    [Fact]
    public async Task SaveAsync_ValidPng_WritesFileAndReturnsUrl() {
        var dir = Path.Combine(Path.GetTempPath(), "synckit-avatar-test-" + Guid.NewGuid());
        var userId = Guid.NewGuid();
        using var content = new MemoryStream([0x89, 0x50, 0x4E, 0x47]);

        var url = await AvatarStore.SaveAsync(dir, userId, content, "image/png", CancellationToken.None);

        Assert.Equal($"/avatars/{userId}", url);
        Assert.True(File.Exists(Path.Combine(dir, $"{userId}.png")));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_UnsupportedContentType_ReturnsNull() {
        var dir = Path.Combine(Path.GetTempPath(), "synckit-avatar-test-" + Guid.NewGuid());
        using var content = new MemoryStream([1, 2, 3]);

        var url = await AvatarStore.SaveAsync(dir, Guid.NewGuid(), content, "application/pdf", CancellationToken.None);

        Assert.Null(url);
    }

    [Fact]
    public async Task SaveAsync_OversizedContent_ReturnsNull() {
        var dir = Path.Combine(Path.GetTempPath(), "synckit-avatar-test-" + Guid.NewGuid());
        using var content = new MemoryStream(new byte[AvatarStore.MaxBytes + 1]);

        var url = await AvatarStore.SaveAsync(dir, Guid.NewGuid(), content, "image/png", CancellationToken.None);

        Assert.Null(url);
    }

    [Fact]
    public void TryGetPath_ExistingFile_ReturnsPathAndContentType() {
        var dir = Path.Combine(Path.GetTempPath(), "synckit-avatar-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var userId = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(dir, $"{userId}.webp"), [1, 2, 3]);
        var found = AvatarStore.TryGetPath(dir, userId, out _, out var contentType);

        Assert.True(found);
        Assert.Equal("image/webp", contentType);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void TryGetPath_MissingFile_ReturnsFalse() {
        var dir = Path.Combine(Path.GetTempPath(), "synckit-avatar-test-" + Guid.NewGuid());

        var found = AvatarStore.TryGetPath(dir, Guid.NewGuid(), out _, out _);

        Assert.False(found);
    }
}
