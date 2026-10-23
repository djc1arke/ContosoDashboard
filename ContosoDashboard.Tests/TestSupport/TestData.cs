using System.IO.Compression;
using System.Text;
using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Tests.TestSupport;

/// <summary>
/// Seed helper. The app's own seed provides: 1 Admin (IT), 2 ProjectManager (Eng, manages project 1),
/// 3 TeamLead (Eng, member of project 1), 4 Employee (Eng, member of project 1).
/// This adds users in a second department and a second project.
/// </summary>
public static class TestData
{
    public const int AdminId = 1;
    public const int ProjectManagerId = 2;      // manages Project 1 (Engineering)
    public const int EngTeamLeadId = 3;         // TeamLead, Engineering, member of Project 1
    public const int EngEmployeeId = 4;         // Employee, Engineering, member of Project 1 (typical uploader)
    public const int SalesEmployeeId = 10;      // Employee, Sales, not in any project
    public const int SalesTeamLeadId = 11;      // TeamLead, Sales
    public const int OtherProjectManagerId = 12; // ProjectManager, Sales, manages Project 10
    public const int EngMemberId = 13;          // Employee, Engineering, member of Project 1 (not uploader)
    public const int Project1 = 1;
    public const int OtherProject = 10;

    public static void Seed(ApplicationDbContext ctx)
    {
        ctx.Users.AddRange(
            NewUser(SalesEmployeeId, "sally.sales@contoso.com", "Sally Sales", "Sales", UserRole.Employee),
            NewUser(SalesTeamLeadId, "tom.lead@contoso.com", "Tom Lead", "Sales", UserRole.TeamLead),
            NewUser(OtherProjectManagerId, "olga.pm@contoso.com", "Olga PM", "Sales", UserRole.ProjectManager),
            NewUser(EngMemberId, "mia.member@contoso.com", "Mia Member", "Engineering", UserRole.Employee));
        ctx.SaveChanges();

        ctx.Projects.Add(new Project
        {
            ProjectId = OtherProject,
            Name = "Sales Outreach",
            ProjectManagerId = OtherProjectManagerId,
            Status = ProjectStatus.Active
        });
        ctx.SaveChanges();

        ctx.ProjectMembers.Add(new ProjectMember { ProjectId = Project1, UserId = EngMemberId, Role = "Developer" });
        ctx.SaveChanges();
    }

    private static User NewUser(int id, string email, string name, string dept, UserRole role) => new()
    {
        UserId = id, Email = email, DisplayName = name, Department = dept, Role = role
    };

    /// <summary>Direct insert of a document row (bypassing the service) for query/authorization tests.</summary>
    public static Document AddDocument(ApplicationDbContext ctx, int uploaderId, string title = "Doc",
        DocumentCategory category = DocumentCategory.Other, int? projectId = null, string fileType = "pdf",
        long size = 1000, DateTime? uploaded = null, string? description = null, params string[] tags)
    {
        var doc = new Document
        {
            Title = title,
            Description = description,
            Category = category,
            OriginalFileName = title + "." + fileType,
            FileType = fileType,
            ContentType = "application/pdf",
            FileSizeBytes = size,
            StoragePath = $"2026/10/{Guid.NewGuid():N}.{fileType}",
            UploadDate = uploaded ?? DateTime.UtcNow,
            UploadedByUserId = uploaderId,
            ProjectId = projectId,
            Tags = tags.Select(t => new DocumentTag { Tag = t }).ToList()
        };
        ctx.Documents.Add(doc);
        ctx.SaveChanges();
        return doc;
    }

    // ---- Sample file content ------------------------------------------------

    public static byte[] Pdf(int extraBytes = 100) => Concat(Encoding.ASCII.GetBytes("%PDF-1.4\n"), new byte[extraBytes]);
    public static byte[] Png() => Concat(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, new byte[64]);
    public static byte[] Jpeg() => Concat(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, new byte[64]);
    public static byte[] Ole2() => Concat(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }, new byte[64]);
    public static byte[] Text(string s = "hello world") => Encoding.UTF8.GetBytes(s);
    public static byte[] Eicar() => Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");

    public static byte[] Ooxml(string rootFolder)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry($"{rootFolder}/document.xml");
            using var w = new StreamWriter(entry.Open());
            w.Write("<x/>");
        }
        return ms.ToArray();
    }

    public static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        a.CopyTo(r, 0);
        b.CopyTo(r, a.Length);
        return r;
    }
}
