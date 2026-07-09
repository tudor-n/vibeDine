using RestaurantApi.Services;
using FluentAssertions;
using Xunit;

namespace RestaurantApi.Tests.Services;

public class ImageValidationTests
{
    [Fact]
    public void SniffImage_DetectsPng()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        ImageValidation.SniffImage(png).Should().Be("image/png");
    }

    [Fact]
    public void SniffImage_DetectsJpeg()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 };
        ImageValidation.SniffImage(jpeg).Should().Be("image/jpeg");
    }

    [Fact]
    public void SniffImage_DetectsWebp()
    {
        var webp = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
        ImageValidation.SniffImage(webp).Should().Be("image/webp");
    }

    [Fact]
    public void SniffImage_RejectsTextMasqueradingAsImage()
    {
        var text = System.Text.Encoding.ASCII.GetBytes("<html>not an ");
        ImageValidation.SniffImage(text).Should().BeNull();
    }
}
