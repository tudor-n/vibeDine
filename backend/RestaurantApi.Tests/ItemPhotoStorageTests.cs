using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantApi.Services;
using RestaurantApi.Storage;
using Xunit;

namespace RestaurantApi.Tests;

public class ItemPhotoStorageTests : IDisposable
{
    private readonly string _contentRoot;
    private readonly ItemPhotoStorage _storage;

    public ItemPhotoStorageTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "item-photo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRoot);
        _storage = new ItemPhotoStorage(
            new UploadPaths(Path.Combine(_contentRoot, "wwwroot", "uploads")),
            NullLogger<ItemPhotoStorage>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_contentRoot, recursive: true); } catch { /* best effort */ }
    }

    private static byte[] ValidJpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };

    [Fact]
    public async Task SaveAsync_ValidJpeg_WritesFileAndReturnsRootRelativeUrl()
    {
        var rid = Guid.NewGuid();

        var url = await _storage.SaveAsync(rid, ValidJpeg(), CancellationToken.None);

        url.Should().NotBeNull();
        url!.Should().StartWith($"/uploads/menu-items/{rid:N}/");
        url.Should().EndWith(".jpg");

        var relative = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(_contentRoot, "wwwroot", relative);
        File.Exists(fullPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(fullPath)).Should().Equal(ValidJpeg());
    }

    [Fact]
    public async Task SaveAsync_PngMagic_ReturnsNull()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A };
        (await _storage.SaveAsync(Guid.NewGuid(), png, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_OversizedPayload_ReturnsNull()
    {
        var big = new byte[(2 * 1024 * 1024) + 1];
        big[0] = 0xFF; big[1] = 0xD8; big[2] = 0xFF;
        (await _storage.SaveAsync(Guid.NewGuid(), big, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_EmptyPayload_ReturnsNull()
    {
        (await _storage.SaveAsync(Guid.NewGuid(), Array.Empty<byte>(), CancellationToken.None)).Should().BeNull();
    }
}
