using Microsoft.EntityFrameworkCore;

namespace BackEnd.Models;

public static class CompanyMembershipQueries
{
    public static IQueryable<AccountsCompany> FirstPerUser(AppDbContext db) =>
        db.AccountsCompanies.Where(candidate => !db.AccountsCompanies.Any(earlier =>
            earlier.AccountId == candidate.AccountId && earlier.CompanyId < candidate.CompanyId));

    public static Task<int?> FirstCompanyId(AppDbContext db, int userId) =>
        db.AccountsCompanies.Where(membership => membership.AccountId == userId)
            .OrderByDescending(membership => membership.CurrentCompanyViewed)
            .ThenBy(membership => membership.CompanyId)
            .Select(membership => (int?)membership.CompanyId).FirstOrDefaultAsync();
}
