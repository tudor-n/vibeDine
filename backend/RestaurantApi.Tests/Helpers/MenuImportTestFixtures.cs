using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Models;

namespace RestaurantApi.Tests.Helpers;

/// <summary>
/// Builds in-memory fixtures (PDF byte arrays, seeded DB tenants) for end-to-end tests.
/// </summary>
public static class MenuImportTestFixtures
{
    /// <summary>Minimal valid PDF byte array (magic bytes + EOF marker).</summary>
    public static byte[] MinimalValidPdf()
    {
        // %PDF-1.4\n%%EOF — enough to pass the magic-byte check in MenuImportController.
        var header = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A };
        var eof    = new byte[] { 0x25, 0x25, 0x45, 0x4F, 0x46 };
        var result = new byte[header.Length + eof.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(eof,    0, result, header.Length, eof.Length);
        return result;
    }

    /// <summary>Builds a multipart form with a minimal valid PDF.</summary>
    public static MultipartFormDataContent ValidPdfMultipart(string fileName = "menu.pdf")
    {
        var bytes   = MinimalValidPdf();
        var form    = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(content, "pdf", fileName);
        return form;
    }

    /// <summary>
    /// Seeds two isolated tenants (orgA→restaurantA→userA, orgB→restaurantB→userB)
    /// and returns the restaurant IDs and pre-built authenticated HTTP clients.
    /// </summary>
    public static async Task<(Guid RestA, Guid RestB, HttpClient ClientA, HttpClient ClientB)>
        SeedTwoTenantsAsync(MenuImportTestFactory factory)
    {
        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var appUserA = new AppUser { Id = userA, UserName = $"a-{userA:N}@test.com", Email = $"a-{userA:N}@test.com", FullName = "Tenant A Admin" };
        var appUserB = new AppUser { Id = userB, UserName = $"b-{userB:N}@test.com", Email = $"b-{userB:N}@test.com", FullName = "Tenant B Admin" };
        await userManager.CreateAsync(appUserA, "TestPass1!");
        await userManager.CreateAsync(appUserB, "TestPass1!");

        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        db.Set<Organization>().AddRange(
            new Organization { Id = orgA, Name = "Org A", Slug = "org-a-" + orgA.ToString("N")[..6], CreatedByUserId = userA },
            new Organization { Id = orgB, Name = "Org B", Slug = "org-b-" + orgB.ToString("N")[..6], CreatedByUserId = userB }
        );
        db.Set<Restaurant>().AddRange(
            new Restaurant { Id = restA, OrganizationId = orgA, Name = "Restaurant A", Slug = "rest-a-" + restA.ToString("N")[..6], IsActive = true },
            new Restaurant { Id = restB, OrganizationId = orgB, Name = "Restaurant B", Slug = "rest-b-" + restB.ToString("N")[..6], IsActive = true }
        );
        await db.SaveChangesAsync();

        db.Set<Membership>().AddRange(
            new Membership { Id = Guid.NewGuid(), UserId = userA, OrganizationId = orgA, RestaurantId = restA, Role = MembershipRole.RestaurantAdmin },
            new Membership { Id = Guid.NewGuid(), UserId = userB, OrganizationId = orgB, RestaurantId = restB, Role = MembershipRole.RestaurantAdmin }
        );
        await db.SaveChangesAsync();

        var clientA = factory.CreateAuthenticatedClient(userA, restA);
        var clientB = factory.CreateAuthenticatedClient(userB, restB);

        return (restA, restB, clientA, clientB);
    }
}
