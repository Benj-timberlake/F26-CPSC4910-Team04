using Microsoft.EntityFrameworkCore;

namespace BackEnd.Models;

public static class CompanyMembershipQueries
{
    public static IQueryable<UserCompanyMembership> All(AppDbContext db) =>
        from membership in db.AccountsCompanies
        join history in db.AccountsHistories on membership.AccountId equals history.Id
        select new UserCompanyMembership(history.UserId, membership.CompanyId, membership.AccountId);

    public static IQueryable<UserCompanyMembership> FirstPerUser(AppDbContext db) =>
        All(db).Where(candidate => !All(db).Any(earlier => earlier.UserId == candidate.UserId &&
            (earlier.AccountId < candidate.AccountId ||
                (earlier.AccountId == candidate.AccountId && earlier.CompanyId < candidate.CompanyId))));

    public static Task<int?> FirstCompanyId(AppDbContext db, int userId) =>
        All(db).Where(membership => membership.UserId == userId)
            .OrderBy(membership => membership.AccountId).ThenBy(membership => membership.CompanyId)
            .Select(membership => (int?)membership.CompanyId).FirstOrDefaultAsync();
}

public sealed record UserCompanyMembership(int UserId, int CompanyId, int AccountId);
