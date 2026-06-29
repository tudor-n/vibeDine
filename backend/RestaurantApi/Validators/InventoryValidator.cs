namespace RestaurantApi.Validators;

public static class InventoryValidator
{
    public const int NameMaxLength = 120;
    public const decimal QuantityMax = 9_999_999.999m;

    public static ValidationResult<string> ValidateName(string? raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (trimmed.Length is < 1 or > NameMaxLength)
            return ValidationResult<string>.Fail($"Numele este obligatoriu și trebuie să aibă 1–{NameMaxLength} caractere.");
        return ValidationResult<string>.Ok(trimmed);
    }

    public static ValidationResult<decimal> ValidateQuantity(decimal value, string field)
    {
        if (value < 0) return ValidationResult<decimal>.Fail($"{field} nu poate fi negativ(ă).");
        if (value > QuantityMax) return ValidationResult<decimal>.Fail($"{field} este prea mare.");
        return ValidationResult<decimal>.Ok(value);
    }

    public static ValidationResult<decimal> ValidatePerPortion(decimal value)
    {
        if (value <= 0) return ValidationResult<decimal>.Fail("Cantitatea pe porție trebuie să fie mai mare decât 0.");
        if (value > QuantityMax) return ValidationResult<decimal>.Fail("Cantitatea pe porție este prea mare.");
        return ValidationResult<decimal>.Ok(value);
    }
}
