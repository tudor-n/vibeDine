using Xunit;
using FluentAssertions;
using RestaurantApi.Validators;

namespace RestaurantApi.Tests;

public class ProductValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_Empty_IsInvalid(string? name)
    {
        var result = ProductValidator.ValidateName(name);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Name");
    }

    [Fact]
    public void Name_TooLong_IsInvalid()
    {
        var result = ProductValidator.ValidateName(new string('a', 121));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Name_Valid_IsTrimmed()
    {
        var result = ProductValidator.ValidateName("  Ciorbă de burtă  ");
        result.IsValid.Should().BeTrue();
        result.Value.Should().Be("Ciorbă de burtă");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99999999.999)]
    [InlineData(100000000)]
    public void Price_OutOfRange_IsInvalid(decimal price)
    {
        ProductValidator.ValidatePrice(price).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Price_InRange_IsValid()
    {
        ProductValidator.ValidatePrice(24.90m).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Description_TooLong_IsInvalid()
    {
        ProductValidator.ValidateDescription(new string('x', 1001)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Description_Null_IsValid()
    {
        ProductValidator.ValidateDescription(null).IsValid.Should().BeTrue();
    }
}
