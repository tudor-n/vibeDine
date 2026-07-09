namespace RestaurantApi.Models;

/// <summary>
/// One line of a recipe-tracked <see cref="Product"/>'s recipe: how much of a given
/// <see cref="Ingredient"/> a single portion of the dish consumes.
/// </summary>
public class RecipeItem : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;

    /// <summary>Amount of the ingredient (in the ingredient's unit) used per single portion.</summary>
    public decimal QuantityPerPortion { get; set; }
}
