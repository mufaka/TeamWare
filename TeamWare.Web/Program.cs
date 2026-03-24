using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Hubs;
using TeamWare.Web.Jobs;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSystemd();

// Add services to the container.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IProjectMemberService, ProjectMemberService>();
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IInboxService, InboxService>();
builder.Services.AddScoped<IProgressService, ProgressService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<IAdminActivityLogService, AdminActivityLogService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IUserDirectoryService, UserDirectoryService>();
builder.Services.AddScoped<IPresenceService, PresenceService>();
builder.Services.AddScoped<IGlobalActivityService, GlobalActivityService>();
builder.Services.AddScoped<IProjectInvitationService, ProjectInvitationService>();
builder.Services.AddScoped<ILoungeService, LoungeService>();
builder.Services.AddScoped<IGlobalConfigurationService, GlobalConfigurationService>();
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<IOllamaService, OllamaService>();
builder.Services.AddScoped<IAiAssistantService, AiAssistantService>();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("Ollama");

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseMemoryStorage());

builder.Services.AddHangfireServer();

builder.Services.AddSignalR();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(Environment.GetEnvironmentVariable("STATE_DIRECTORY")
            ?? builder.Environment.ContentRootPath, "keys")));

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["text/html"]);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/StatusCode/{0}");
app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<PresenceHub>("/hubs/presence");
app.MapHub<LoungeHub>("/hubs/lounge");

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthorizationFilter()]
});

// Seed the admin account on first run
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await SeedData.InitializeAsync(services);
}

// Register Hangfire recurring jobs
var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();
recurringJobManager.AddOrUpdate<LoungeRetentionJob>(
    "lounge-retention-cleanup",
    job => job.Execute(),
    Cron.Daily);

recurringJobManager.AddOrUpdate<TaskDueDateJob>(
    "task-due-date-promotion",
    job => job.Execute(),
    Cron.Daily);

app.Run();

// Make the implicit Program class accessible to the test project
public partial class Program { }
