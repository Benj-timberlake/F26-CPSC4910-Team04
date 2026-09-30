using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class AboutEndpoints
{
    public static void MapAboutEndpoints(this WebApplication app)
    {
        app.MapGet("/api/about", async (AppDbContext db) =>
        {
            var rows = await db.AboutPages.AsNoTracking()
                .OrderByDescending(page => page.Type == "main")
                .ThenBy(page => page.Id)
                .Select(page => new AboutPageContent(page.Type, page.Title, page.Body))
                .ToListAsync();

            return Results.Ok(rows);
        });
    }
}

public record AboutPageContent(string Type, string Title, string Body);
