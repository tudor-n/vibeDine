using Microsoft.EntityFrameworkCore;
using RestaurantApi.Models;

namespace RestaurantApi.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        // Only seed if the database is empty
        if (await db.Categories.AnyAsync())
            return;

        // ── Categories ───────────────────────────────────────────────
        var feluri = new Category { Id = Guid.NewGuid(), Name = "Feluri Principale", SortOrder = 1 };
        var salate = new Category { Id = Guid.NewGuid(), Name = "Salate",            SortOrder = 2 };
        var supe   = new Category { Id = Guid.NewGuid(), Name = "Supe & Ciorbe",     SortOrder = 3 };
        var bauturi = new Category { Id = Guid.NewGuid(), Name = "Băuturi",          SortOrder = 4 };
        var deserturi = new Category { Id = Guid.NewGuid(), Name = "Deserturi",      SortOrder = 5 };

        await db.Categories.AddRangeAsync(feluri, salate, supe, bauturi, deserturi);

        // ── Products ─────────────────────────────────────────────────
        var products = new List<Product>
        {
            // Feluri principale
            new() {
                Id = Guid.NewGuid(), CategoryId = feluri.Id,
                Name = "Ceafă de porc la grătar",
                Description = "Ceafă marinată, servită cu cartofi prăjiți și murături",
                Price = 45.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = feluri.Id,
                Name = "Șnițel de pui pane",
                Description = "Piept de pui crocant în pesmet panko, cu piure de cartofi",
                Price = 38.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = feluri.Id,
                Name = "Tochitura moldovenească",
                Description = "Porc, cârnați, ou, mămăligă și brânză de burduf",
                Price = 52.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = feluri.Id,
                Name = "Pastrav la grătar",
                Description = "Pastrav întreg cu lămâie, usturoi și legume la grătar",
                Price = 58.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = feluri.Id,
                Name = "Mici tradiționali",
                Description = "Porție de 6 mici din carne de vită și porc, cu muștar și pâine",
                Price = 32.00m, ImageUrl = null, IsAvailable = true
            },

            // Salate
            new() {
                Id = Guid.NewGuid(), CategoryId = salate.Id,
                Name = "Salată Caesar",
                Description = "Salată romană, crutoane, parmezan, dressing Caesar clasic",
                Price = 28.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = salate.Id,
                Name = "Salată grecească",
                Description = "Roșii, castraveți, măsline, ceapă roșie, feta",
                Price = 24.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = salate.Id,
                Name = "Salată cu pui grătar",
                Description = "Mix de verdețuri, piept de pui, roșii cherry, dressing balsamic",
                Price = 32.00m, ImageUrl = null, IsAvailable = true
            },

            // Supe & Ciorbe
            new() {
                Id = Guid.NewGuid(), CategoryId = supe.Id,
                Name = "Ciorbă de burtă",
                Description = "Rețetă tradițională, cu smântână și ardei iute",
                Price = 22.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = supe.Id,
                Name = "Ciorbă de legume",
                Description = "Sezonieră, cu tarhon și leuștean proaspăt",
                Price = 18.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = supe.Id,
                Name = "Supă cremă de roșii",
                Description = "Cu busuioc proaspăt și crutoane cu parmezan",
                Price = 20.00m, ImageUrl = null, IsAvailable = true
            },

            // Băuturi
            new() {
                Id = Guid.NewGuid(), CategoryId = bauturi.Id,
                Name = "Limonadă cu mentă (400ml)",
                Description = "Lămâie proaspătă, miere, mentă, apă minerală",
                Price = 18.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = bauturi.Id,
                Name = "Bere Ursus Premium (500ml)",
                Description = "La sticlă, 5% alcool",
                Price = 12.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = bauturi.Id,
                Name = "Vin roșu casa (250ml)",
                Description = "Fetească Neagră, cramă locală",
                Price = 16.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = bauturi.Id,
                Name = "Apă minerală (500ml)",
                Description = "Borsec sau Bucovina",
                Price = 8.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = bauturi.Id,
                Name = "Cafea espresso",
                Description = "Boabe 100% Arabica",
                Price = 10.00m, ImageUrl = null, IsAvailable = true
            },

            // Deserturi
            new() {
                Id = Guid.NewGuid(), CategoryId = deserturi.Id,
                Name = "Papanași prăjiți",
                Description = "Cu smântână și dulceață de vișine",
                Price = 22.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = deserturi.Id,
                Name = "Tiramisu",
                Description = "Rețetă italiană clasică cu mascarpone și cafea",
                Price = 24.00m, ImageUrl = null, IsAvailable = true
            },
            new() {
                Id = Guid.NewGuid(), CategoryId = deserturi.Id,
                Name = "Clătite cu nutella",
                Description = "Două clătite cu nutella și frișcă",
                Price = 20.00m, ImageUrl = null, IsAvailable = false // sezonier — indisponibil
            },
        };

        await db.Products.AddRangeAsync(products);

        // ── Tables ───────────────────────────────────────────────────
       var tables = new List<RestaurantTable>
{
    new() { Id = Guid.NewGuid(), TableNumber = "1", QrToken = Guid.Parse("11111111-1111-1111-1111-111111111111") },
    new() { Id = Guid.NewGuid(), TableNumber = "2", QrToken = Guid.NewGuid() },
    new() { Id = Guid.NewGuid(), TableNumber = "3", QrToken = Guid.NewGuid() },
    new() { Id = Guid.NewGuid(), TableNumber = "4", QrToken = Guid.NewGuid() },
};

        await db.Tables.AddRangeAsync(tables);

        // ── Default AI Directive ─────────────────────────────────────
        var directive = new AIDirective
        {
            Id = Guid.NewGuid(),
            RawPrompt = "Recomandă papanași și tiramisu la finalul oricărei comenzi. " +
                        "Promovează vinul roșu al casei cu preparatele la grătar.",
            ParsedRules = null,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        await db.AIDirectives.AddAsync(directive);

        await db.SaveChangesAsync();
    }
}