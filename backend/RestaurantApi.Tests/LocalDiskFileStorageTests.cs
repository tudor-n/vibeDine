using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantApi.Storage;
using Xunit;

namespace RestaurantApi.Tests;

/// <summary>
/// Unit tests for LocalDiskFileStorage — no database, no HTTP, pure file I/O.
/// Each test gets its own unique temp directory to avoid cross-test pollution.
/// </summary>
public class LocalDiskFileStorageTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalDiskFileStorage _sut;

    public LocalDiskFileStorageTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _sut = BuildSut(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static LocalDiskFileStorage BuildSut(string root)
    {
        var config = new ConfigurationBuilder()
            .Add(new MemoryConfigurationSource
            {
                InitialData = new Dictionary<string, string?>
                {
                    ["MenuImport:StorageRoot"] = root
                }
            })
            .Build();

        return new LocalDiskFileStorage(config, NullLogger<LocalDiskFileStorage>.Instance);
    }

    private static async Task<string> WriteTestFileAsync(
        LocalDiskFileStorage sut,
        Guid restaurantId,
        Guid jobId,
        byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return await sut.WriteAsync(restaurantId, jobId, "menu.pdf", ms, CancellationToken.None);
    }

    // ── 1. WriteThenOpenRoundTrip ─────────────────────────────────────────────

    [Fact]
    public async Task WriteThenOpenRoundTrip_BytesReadBackIdentically()
    {
        var restaurantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var original = Encoding.UTF8.GetBytes("PDF content for round-trip test");

        var key = await WriteTestFileAsync(_sut, restaurantId, jobId, original);

        await using var stream = await _sut.OpenAsync(key, CancellationToken.None);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        ms.ToArray().Should().Equal(original);
    }

    [Fact]
    public async Task WriteAsync_ReturnsCorrectStorageKey()
    {
        var restaurantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var key = await WriteTestFileAsync(_sut, restaurantId, jobId, Array.Empty<byte>());

        var expected = $"{restaurantId:N}/{jobId:N}.pdf";
        key.Should().Be(expected);
    }

    // ── 2. DeleteIsIdempotent ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteIsIdempotent_NoExceptionWhenFileAbsent()
    {
        var nonExistentKey = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}.pdf";

        var act = () => _sut.DeleteAsync(nonExistentKey, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // ── 3. DeleteRemovesFile ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRemovesFile_OpenAfterDeleteThrows()
    {
        var restaurantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes("temporary data");

        var key = await WriteTestFileAsync(_sut, restaurantId, jobId, bytes);
        await _sut.DeleteAsync(key, CancellationToken.None);

        var act = async () => await _sut.OpenAsync(key, CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    // ── 4. TenantKeyIsolation ─────────────────────────────────────────────────

    [Fact]
    public async Task TenantKeyIsolation_DifferentTenantsProduceDifferentKeysAndPaths()
    {
        var jobId = Guid.NewGuid();
        var restaurantA = Guid.NewGuid();
        var restaurantB = Guid.NewGuid();

        var keyA = await WriteTestFileAsync(_sut, restaurantA, jobId, new byte[] { 0xAA });
        var keyB = await WriteTestFileAsync(_sut, restaurantB, jobId, new byte[] { 0xBB });

        keyA.Should().NotBe(keyB, "different restaurant IDs must produce different storage keys");

        var pathA = _sut.ResolveLocalPath(keyA);
        var pathB = _sut.ResolveLocalPath(keyB);
        pathA.Should().NotBe(pathB, "different storage keys must map to different physical paths");
    }

    // ── 5. PathTraversalRejected ──────────────────────────────────────────────

    [Fact]
    public void PathTraversalRejected_ThrowsInvalidOperationException()
    {
        var traversalKey = "../../escape.pdf";

        var act = () => _sut.ResolveLocalPath(traversalKey);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("Invalid storage key.");
    }

    // ── 6. CustomStorageRootRespected ─────────────────────────────────────────

    [Fact]
    public async Task CustomStorageRootRespected_FileIsWrittenUnderConfiguredRoot()
    {
        var customRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var sut = BuildSut(customRoot);
            var restaurantId = Guid.NewGuid();
            var jobId = Guid.NewGuid();

            var key = await WriteTestFileAsync(sut, restaurantId, jobId, new byte[] { 0x01 });

            var resolved = sut.ResolveLocalPath(key);
            resolved.Should().StartWith(Path.GetFullPath(customRoot),
                "the written file must reside under the configured storage root");
            File.Exists(resolved).Should().BeTrue("WriteAsync must have actually created the file");
        }
        finally
        {
            if (Directory.Exists(customRoot))
                Directory.Delete(customRoot, recursive: true);
        }
    }
}
