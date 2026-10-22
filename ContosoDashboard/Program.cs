using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Add authentication state provider for Blazor
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();

// Configure Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure Mock Authentication (Cookie-based for training purposes)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// Add authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Employee", policy => policy.RequireRole("Employee", "TeamLead", "ProjectManager", "Administrator"));
    options.AddPolicy("TeamLead", policy => policy.RequireRole("TeamLead", "ProjectManager", "Administrator"));
    options.AddPolicy("ProjectManager", policy => policy.RequireRole("ProjectManager", "Administrator"));
    options.AddPolicy("Administrator", policy => policy.RequireRole("Administrator"));
});

// Register application services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

// Document management infrastructure
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
builder.Services.AddSingleton<IMalwareScanService, StubMalwareScanService>();
builder.Services.AddScoped<IDocumentActivityService, DocumentActivityService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();

// Add HttpContextAccessor for accessing user claims
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.EnsureCreated(); // For development - use migrations in production
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred creating the database.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    // Use HSTS even in development for training purposes
    app.UseHsts();
}

// Add security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    
    // Content Security Policy for Blazor Server
    context.Response.Headers["Content-Security-Policy"] = 
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "font-src 'self' https://cdn.jsdelivr.net; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self' wss: ws:;";
    
    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Enable authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();

// Document delivery endpoints (contracts/http-endpoints.md)
app.MapGet("/documents/{id:int}/download",
        (int id, HttpContext http, IDocumentService documents, CancellationToken ct)
            => DocumentDelivery.ServeAsync(id, http, documents, DocumentAccessKind.Download, ct))
    .RequireAuthorization("Employee");
app.MapGet("/documents/{id:int}/preview",
        (int id, HttpContext http, IDocumentService documents, CancellationToken ct)
            => DocumentDelivery.ServeAsync(id, http, documents, DocumentAccessKind.Preview, ct))
    .RequireAuthorization("Employee");

app.MapFallbackToPage("/_Host");

app.Run();

// Exposed for WebApplicationFactory-based tests.
public partial class Program { }

static class DocumentDelivery
{
    public static async Task<IResult> ServeAsync(int id, HttpContext http, IDocumentService documents,
        DocumentAccessKind kind, CancellationToken ct)
    {
        var claim = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(claim, out var userId)) return Results.NotFound();

        var doc = await documents.OpenReadAsync(id, userId, kind, ct);
        if (doc == null) return Results.NotFound();

        var name = doc.FileName;
        var ascii = new string(name.Select(c => c < 0x20 || c > 0x7E || c == '"' || c == '\\' ? '_' : c).ToArray());
        var disposition = kind == DocumentAccessKind.Preview ? "inline" : "attachment";
        var headers = http.Response.Headers;
        headers["Content-Disposition"] = $"{disposition}; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Cache-Control"] = "private, no-store";
        headers["Content-Length"] = doc.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return Results.Stream(doc.Stream, doc.ContentType);
    }
}
