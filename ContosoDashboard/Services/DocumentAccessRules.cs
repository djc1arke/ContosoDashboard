using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

/// <summary>
/// Single, central, live-evaluated document authorization rules (research R7).
/// Role and department always come from the database for the requesting user, never from claims.
/// </summary>
public static class DocumentAccessRules
{
    /// <summary>Loads the requester's current role/department. Null if the user does not exist.</summary>
    public static Task<User?> LoadRequesterAsync(ApplicationDbContext context, int userId)
        => context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);

    /// <summary>Documents the requester may view/preview/download.</summary>
    public static IQueryable<Document> Visible(IQueryable<Document> documents, User requester)
    {
        var userId = requester.UserId;
        if (requester.Role == UserRole.Administrator) return documents;

        var dept = requester.Department;
        var isTeamLead = requester.Role == UserRole.TeamLead && dept != null;

        return documents.Where(d =>
            d.UploadedByUserId == userId
            || (d.ProjectId != null && d.Project!.ProjectManagerId == userId)
            || (d.ProjectId != null && d.Project!.ProjectMembers.Any(pm => pm.UserId == userId))
            || d.Shares.Any(s => s.SharedWithUserId == userId)
            || (dept != null && d.Shares.Any(s => s.SharedWithTeam == dept))
            || (isTeamLead && d.UploadedByUser.Department == dept));
    }

    /// <summary>Edit metadata / delete. Document must have <c>UploadedByUser</c> and <c>Project</c> loaded.</summary>
    public static bool CanEditOrDelete(User requester, Document document)
    {
        if (requester.Role == UserRole.Administrator) return true;
        if (document.UploadedByUserId == requester.UserId) return true;
        if (document.ProjectId != null && document.Project?.ProjectManagerId == requester.UserId) return true;
        return requester.Role == UserRole.TeamLead
               && requester.Department != null
               && document.UploadedByUser?.Department == requester.Department;
    }

    /// <summary>Replace file: uploader only (Administrator excluded).</summary>
    public static bool CanReplace(User requester, Document document)
        => document.UploadedByUserId == requester.UserId;

    /// <summary>Share / unshare: uploader only (Administrator excluded).</summary>
    public static bool CanShare(User requester, Document document)
        => document.UploadedByUserId == requester.UserId;

    /// <summary>Project association: member/manager of that project; Administrator any project.</summary>
    public static async Task<bool> CanAssociateProjectAsync(ApplicationDbContext context, User requester, int projectId)
    {
        if (requester.Role == UserRole.Administrator)
            return await context.Projects.AnyAsync(p => p.ProjectId == projectId);

        return await context.Projects.AnyAsync(p => p.ProjectId == projectId
            && (p.ProjectManagerId == requester.UserId
                || p.ProjectMembers.Any(pm => pm.UserId == requester.UserId)));
    }
}
