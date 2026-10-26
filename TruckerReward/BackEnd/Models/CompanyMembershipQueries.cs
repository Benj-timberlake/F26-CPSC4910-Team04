using Microsoft.EntityFrameworkCore;

namespace BackEnd.Models;

public static class CompanyMembershipQueries
{
    public static IQueryable<AccountsCompany> FirstPerUser(AppDbContext db) =>
        db.AccountsCompanies.Where(candidate => !db.AccountsCompanies.Any(earlier =>
            earlier.Account.UserId == candidate.Account.UserId &&
            (earlier.AccountId < candidate.AccountId ||
                (earlier.AccountId == candidate.AccountId && earlier.CompanyId < candidate.CompanyId))));

    public static Task<int?> FirstCompanyId(AppDbContext db, int userId) =>
        db.AccountsCompanies.Where(membership => membership.Account.UserId == userId)
            .OrderBy(membership => membership.AccountId).ThenBy(membership => membership.CompanyId)
            .Select(membership => (int?)membership.CompanyId).FirstOrDefaultAsync();
}
