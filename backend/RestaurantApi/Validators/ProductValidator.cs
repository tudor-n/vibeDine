namespace RestaurantApi.Validators;

public readonly record struct ValidationResult<T>(bool IsValid, T? Value, string? Error)
{
    public static ValidationResult<T> Ok(T value) => new(true, value, null);
    public static ValidationResult<T> Fail(string error) => new(false, default, error);
}

public static class ProductValidator
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 1000;
    public const int ImageUrlMaxLength = 500;
    public const decimal PriceMax = 99999999.99m;

    public static ValidationResult<string> ValidateName(string? raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (trimmed.Length is < 1 or > NameMaxLength)
            return ValidationResult<string>.Fail($"Name is required and must be 1–{NameMaxLength} characters.");
        return ValidationResult<string>.Ok(trimmed);
    }

    public static ValidationResult<decimal> ValidatePrice(decimal price)
    {
        if (price <= 0 || price > PriceMax)
            return ValidationResult<decimal>.Fail($"Price must be greater than 0 and at most {PriceMax}.");
        return ValidationResult<decimal>.Ok(price);
    }

    public static ValidationResult<string?> ValidateDescription(string? raw)
    {
        if (raw is null) return ValidationResult<string?>.Ok(null);
        if (raw.Length > DescriptionMaxLength)
            return ValidationResult<string?>.Fail($"Description must be at most {DescriptionMaxLength} characters.");
        return ValidationResult<string?>.Ok(raw);
    }

    public static ValidationResult<string?> ValidateImageUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ValidationResult<string?>.Ok(null);
        if (raw.Length > ImageUrlMaxLength)
            return ValidationResult<string?>.Fail($"ImageUrl must be at most {ImageUrlMaxLength} characters.");

        // Accept root-relative paths (e.g. "/uploads/menu-items/{id}.jpg") so locally
        // stored crops validate, while rejecting protocol-relative "//host/x" which would
        // resolve to an external origin. Absolute http(s) URLs keep working.
        if (raw.StartsWith('/'))
        {
            if (raw.StartsWith("//"))
                return ValidationResult<string?>.Fail("ImageUrl must be an absolute URL or a root-relative path.");
            if (raw.Any(char.IsWhiteSpace))
                return ValidationResult<string?>.Fail("ImageUrl must not contain whitespace.");
            return ValidationResult<string?>.Ok(raw);
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out _))
            return ValidationResult<string?>.Fail("ImageUrl must be a valid absolute URL or root-relative path.");
        return ValidationResult<string?>.Ok(raw);
    }
}
