using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// security notices go to every admin account
public static class Notify
{
    public static async Task Admins(AppDbContext db, IEmailSender email, string subject, string body)
    {
        var admins = await db.Users.AsNoTracking()
            .Where(u => u.UserType == AuthEndpoints.Admin)
            .Select(u => u.Email)
            .ToListAsync();
        foreach (var to in admins)
            await email.SendAsync(to, subject, body);
    }
}
