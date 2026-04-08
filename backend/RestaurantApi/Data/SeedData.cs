using Microsoft.EntityFrameworkCore;
using RestaurantApi.Models;

namespace RestaurantApi.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        // Only seed if the database is empty
        if (await db.Categories.AnyAsync()) return;

        // ── Categories ───────────────────────────────────────────────────────────
        var feluri     = new Category { Id = Guid.Parse("11111111-0000-0000-0000-000000000001"), Name = "Feluri principale",  SortOrder = 1 };
        var supe       = new Category { Id = Guid.Parse("11111111-0000-0000-0000-000000000002"), Name = "Supe & Ciorbe",      SortOrder = 2 };
        var salate     = new Category { Id = Guid.Parse("11111111-0000-0000-0000-000000000003"), Name = "Salate",             SortOrder = 3 };
        var deserturi  = new Category { Id = Guid.Parse("11111111-0000-0000-0000-000000000004"), Name = "Deserturi",          SortOrder = 4 };
        var bauturi    = new Category { Id = Guid.Parse("11111111-0000-0000-0000-000000000005"), Name = "Băuturi",            SortOrder = 5 };

        await db.Categories.AddRangeAsync(feluri, supe, salate, deserturi, bauturi);

        // ── Products ─────────────────────────────────────────────────────────────

        // Feluri principale
        var prod01 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000001"),
            CategoryId = feluri.Id,
            Name = "Grătar mixt",
            Description = "Ceafă de porc, mici și cârnați, servite cu cartofi prăjiți și murături",
            Price = 52.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod02 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000002"),
            CategoryId = feluri.Id,
            Name = "Șnițel de pui",
            Description = "Piept de pui pane în pesmet panko, servit cu piure de cartofi",
            Price = 38.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod03 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000003"),
            CategoryId = feluri.Id,
            Name = "Tochitura moldovenească",
            Description = "Carne de porc cu mămăligă, ou ochi și brânză telemea",
            Price = 45.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod04 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000004"),
            CategoryId = feluri.Id,
            Name = "Pastă cu pui și ciuperci",
            Description = "Penne cu piept de pui, ciuperci champignon și sos de smântână",
            Price = 35.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod05 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000005"),
            CategoryId = feluri.Id,
            Name = "File de salău la grătar",
            Description = "File de salău cu legume la grătar și sos de lămâie cu unt",
            Price = 55.00m,
            ImageUrl = null,
            IsAvailable = false
        };

        // Supe & Ciorbe
        var prod06 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000006"),
            CategoryId = supe.Id,
            Name = "Ciorbă de burtă",
            Description = "Ciorbă tradițională cu burtă de vită, smântână și usturoi",
            Price = 22.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod07 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000007"),
            CategoryId = supe.Id,
            Name = "Ciorbă de fasole cu afumătură",
            Description = "Fasole boabe cu ciolan afumat și tarhon",
            Price = 18.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod08 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000008"),
            CategoryId = supe.Id,
            Name = "Supă cremă de dovleac",
            Description = "Cremă de dovleac cu semințe de dovleac prăjite și ulei de trufe",
            Price = 20.00m,
            ImageUrl = null,
            IsAvailable = true
        };

        // Salate
        var prod09 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000009"),
            CategoryId = salate.Id,
            Name = "Salată Caesar cu pui",
            Description = "Salată romană, piept de pui la grătar, crutoane, parmezan și sos Caesar",
            Price = 28.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod10 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000010"),
            CategoryId = salate.Id,
            Name = "Salată grecească",
            Description = "Roșii, castraveți, măsline kalamata, brânză feta și oregano",
            Price = 24.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod11 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000011"),
            CategoryId = salate.Id,
            Name = "Salată de vinete",
            Description = "Vinete coapte cu ceapă, roșii și ulei de floarea soarelui",
            Price = 16.00m,
            ImageUrl = null,
            IsAvailable = true
        };

        // Deserturi
        var prod12 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000012"),
            CategoryId = deserturi.Id,
            Name = "Papanași prăjiți",
            Description = "Papanași tradiționali cu smântână și dulceață de afine",
            Price = 22.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod13 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000013"),
            CategoryId = deserturi.Id,
            Name = "Clătite cu nutella",
            Description = "Două clătite subțiri cu cremă de nutella și frișcă",
            Price = 18.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod14 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000014"),
            CategoryId = deserturi.Id,
            Name = "Tiramisu",
            Description = "Rețetă clasică italiană cu mascarpone, espresso și cacao",
            Price = 20.00m,
            ImageUrl = null,
            IsAvailable = true
        };

        // Băuturi
        var prod15 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000015"),
            CategoryId = bauturi.Id,
            Name = "Limonadă cu mentă",
            Description = "Lămâie proaspătă, miere, mentă și apă carbogazoasă, 400ml",
            Price = 14.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod16 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000016"),
            CategoryId = bauturi.Id,
            Name = "Bere Ursus Premium",
            Description = "La sticlă, 500ml",
            Price = 12.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod17 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000017"),
            CategoryId = bauturi.Id,
            Name = "Vin roșu casa (250ml)",
            Description = "Vin roșu demisec de casă, produs local",
            Price = 16.00m,
            ImageUrl = null,
            IsAvailable = true
        };
        var prod18 = new Product
        {
            Id = Guid.Parse("22222222-0000-0000-0000-000000000018"),
            CategoryId = bauturi.Id,
            Name = "Apă minerală",
            Description = "Apă minerală naturală, 500ml",
            Price = 8.00m,
            ImageUrl = null,
            IsAvailable = true
        };

        await db.Products.AddRangeAsync(
            prod01, prod02, prod03, prod04, prod05,
            prod06, prod07, prod08,
            prod09, prod10, prod11,
            prod12, prod13, prod14,
            prod15, prod16, prod17, prod18
        );

        // ── Tables ───────────────────────────────────────────────────────────────
        var tables = new[]
        {
            new RestaurantTable
            {
                Id = Guid.Parse("33333333-0000-0000-0000-000000000001"),
                TableNumber = "1",
                QrToken = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")
            },
            new RestaurantTable
            {
                Id = Guid.Parse("33333333-0000-0000-0000-000000000002"),
                TableNumber = "2",
                QrToken = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002")
            },
            new RestaurantTable
            {
                Id = Guid.Parse("33333333-0000-0000-0000-000000000003"),
                TableNumber = "3",
                QrToken = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003")
            },
            new RestaurantTable
            {
                Id = Guid.Parse("33333333-0000-0000-0000-000000000004"),
                TableNumber = "4",
                QrToken = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004")
            }
        };

        await db.Tables.AddRangeAsync(tables);

        await db.SaveChangesAsync();
    }
}
