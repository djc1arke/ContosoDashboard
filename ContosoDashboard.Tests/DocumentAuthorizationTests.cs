using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;

namespace ContosoDashboard.Tests;

public class DocumentAuthorizationTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ApplicationDbContext _ctx;

    public DocumentAuthorizationTests() => _ctx = _db.CreateContext();

    public void Dispose() { _ctx.Dispose(); _db.Dispose(); }

    private User U(int id) => _ctx.Users.AsNoTracking().First(u => u.UserId == id);

    private bool CanView(int userId, Document d)
        => DocumentAccessRules.Visible(_ctx.Documents.AsNoTracking(), U(userId)).Any(x => x.DocumentId == d.DocumentId);

    private Document Loaded(Document d)
        => _ctx.Documents.AsNoTracking().Include(x => x.UploadedByUser).Include(x => x.Project).First(x => x.DocumentId == d.DocumentId);

    [Fact]
    public void Personal_document_visible_to_uploader_and_admin_only()
    {
        var d = TestData.AddDocument(_ctx, TestData.EngEmployeeId, projectId: null);

        Assert.True(CanView(TestData.EngEmployeeId, d));
        Assert.True(CanView(TestData.AdminId, d));
        Assert.True(CanView(TestData.EngTeamLeadId, d));   // same-department Team Lead
        // Unauthorized paths
        Assert.False(CanView(TestData.SalesEmployeeId, d));
        Assert.False(CanView(TestData.SalesTeamLeadId, d)); // other-department Team Lead
        Assert.False(CanView(TestData.OtherProjectManagerId, d));
        Assert.False(CanView(TestData.EngMemberId, d));     // same dept but plain employee, no project
    }

    [Fact]
    public void Project_document_visible_to_manager_and_members_not_to_other_project_manager()
    {
        var d = TestData.AddDocument(_ctx, TestData.EngEmployeeId, projectId: TestData.Project1);

        Assert.True(CanView(TestData.ProjectManagerId, d));
        Assert.True(CanView(TestData.EngMemberId, d));
        Assert.False(CanView(TestData.OtherProjectManagerId, d));
        Assert.False(CanView(TestData.SalesEmployeeId, d));
    }

    [Fact]
    public void Direct_share_and_team_share_grant_view_only_to_recipients()
    {
        var d = TestData.AddDocument(_ctx, TestData.EngEmployeeId);
        _ctx.DocumentShares.Add(new DocumentShare { DocumentId = d.DocumentId, SharedWithUserId = TestData.SalesEmployeeId, SharedByUserId = TestData.EngEmployeeId });
        _ctx.SaveChanges();

        Assert.True(CanView(TestData.SalesEmployeeId, d));
        Assert.False(CanView(TestData.SalesTeamLeadId, d));

        _ctx.DocumentShares.Add(new DocumentShare { DocumentId = d.DocumentId, SharedWithTeam = "Sales", SharedByUserId = TestData.EngEmployeeId });
        _ctx.SaveChanges();
        Assert.True(CanView(TestData.SalesTeamLeadId, d));
        Assert.True(CanView(TestData.OtherProjectManagerId, d)); // Sales department
        Assert.False(CanView(TestData.EngMemberId, d));
    }

    [Fact]
    public void Share_recipient_cannot_manage()
    {
        var d = TestData.AddDocument(_ctx, TestData.EngEmployeeId);
        var loaded = Loaded(d);
        var recipient = U(TestData.SalesEmployeeId);
        Assert.False(DocumentAccessRules.CanEditOrDelete(recipient, loaded));
        Assert.False(DocumentAccessRules.CanReplace(recipient, loaded));
        Assert.False(DocumentAccessRules.CanShare(recipient, loaded));
    }

    [Fact]
    public void Management_matrix()
    {
        var d = TestData.AddDocument(_ctx, TestData.EngEmployeeId, projectId: TestData.Project1);
        var doc = Loaded(d);

        // Edit/delete allowed: uploader, admin, project manager, same-dept team lead
        foreach (var id in new[] { TestData.EngEmployeeId, TestData.AdminId, TestData.ProjectManagerId, TestData.EngTeamLeadId })
            Assert.True(DocumentAccessRules.CanEditOrDelete(U(id), doc), $"user {id}");

        // Denied: project member, other-dept lead, other project's manager, outsider
        foreach (var id in new[] { TestData.EngMemberId, TestData.SalesTeamLeadId, TestData.OtherProjectManagerId, TestData.SalesEmployeeId })
            Assert.False(DocumentAccessRules.CanEditOrDelete(U(id), doc), $"user {id}");

        // Replace / share: uploader only; Administrator cannot
        Assert.True(DocumentAccessRules.CanReplace(U(TestData.EngEmployeeId), doc));
        Assert.True(DocumentAccessRules.CanShare(U(TestData.EngEmployeeId), doc));
        foreach (var id in new[] { TestData.AdminId, TestData.ProjectManagerId, TestData.EngTeamLeadId })
        {
            Assert.False(DocumentAccessRules.CanReplace(U(id), doc), $"replace {id}");
            Assert.False(DocumentAccessRules.CanShare(U(id), doc), $"share {id}");
        }
    }

    [Fact]
    public async Task Project_association_rules()
    {
        Assert.True(await DocumentAccessRules.CanAssociateProjectAsync(_ctx, U(TestData.EngMemberId), TestData.Project1));
        Assert.True(await DocumentAccessRules.CanAssociateProjectAsync(_ctx, U(TestData.ProjectManagerId), TestData.Project1));
        Assert.True(await DocumentAccessRules.CanAssociateProjectAsync(_ctx, U(TestData.AdminId), TestData.OtherProject));
        Assert.False(await DocumentAccessRules.CanAssociateProjectAsync(_ctx, U(TestData.SalesEmployeeId), TestData.Project1));
        Assert.False(await DocumentAccessRules.CanAssociateProjectAsync(_ctx, U(TestData.OtherProjectManagerId), TestData.Project1));
        Assert.False(await DocumentAccessRules.CanAssociateProjectAsync(_ctx, U(TestData.AdminId), 9999));
    }

    [Fact]
    public void Rules_are_evaluated_live_after_membership_and_department_changes()
    {
        var d = TestData.AddDocument(_ctx, TestData.EngEmployeeId, projectId: TestData.Project1);
        Assert.True(CanView(TestData.EngMemberId, d));

        // Remove membership -> access lost immediately
        var membership = _ctx.ProjectMembers.First(pm => pm.UserId == TestData.EngMemberId);
        _ctx.ProjectMembers.Remove(membership);
        _ctx.SaveChanges();
        Assert.False(CanView(TestData.EngMemberId, d));

        // Team Lead moves department -> loses same-department visibility
        var personal = TestData.AddDocument(_ctx, TestData.EngEmployeeId);
        Assert.True(CanView(TestData.EngTeamLeadId, personal));
        var lead = _ctx.Users.First(u => u.UserId == TestData.EngTeamLeadId);
        lead.Department = "Sales";
        _ctx.SaveChanges();
        Assert.False(CanView(TestData.EngTeamLeadId, personal));
    }

    [Fact]
    public void Share_check_constraint_rejects_both_or_neither_target()
    {
        var d = TestData.AddDocument(_ctx, TestData.EngEmployeeId);
        _ctx.DocumentShares.Add(new DocumentShare { DocumentId = d.DocumentId, SharedByUserId = TestData.EngEmployeeId });
        Assert.Throws<DbUpdateException>(() => _ctx.SaveChanges());
    }
}
