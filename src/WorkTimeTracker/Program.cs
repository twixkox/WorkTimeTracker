using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using WorkTimeTracker;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Security;
using WorkTimeTracker.Services;
using WorkTimeTracker.Telegram;

var builder = WebApplication.CreateBuilder(args);

var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

// Русский текст в HTML не кодируется в числовые сущности.
builder.Services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));

// Провайдер БД: "Sqlite" (по умолчанию, файл worktime.db) или "SqlServer" (MSSQL).
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Строка подключения 'Default' не найдена.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    _ = provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
        ? options.UseSqlServer(connectionString)
        : options.UseSqlite(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 6;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<RussianIdentityErrorDescriber>();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Employee", "EmployeeOrMaster");
    options.Conventions.AuthorizeFolder("/Master", "MasterOrAbove");
    options.Conventions.AuthorizeFolder("/Production", "ProductionOrAdmin");
    options.Conventions.AuthorizeFolder("/Engineering", "EngineeringAccess");
    options.Conventions.AuthorizeFolder("/Admin", "AdministrationAccess");
    // Регистрация новых учётных записей — только для администратора.
    options.Conventions.AuthorizeAreaPage("Identity", "/Account/Register", "AdministrationAccess");
})
.AddMvcOptions(options =>
{
    options.ModelBinderProviders.Insert(0, new WorkTimeTracker.Services.DecimalModelBinderProvider());
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/AccessDenied";
});

builder.Services.AddAuthorization(options =>
{
    foreach (var role in AppRoles.All)
        options.AddPolicy(role, policy => policy.RequireRole(role));
    options.AddPolicy("EmployeeOrMaster", policy =>
        policy.Requirements.Add(new PermissionRequirement(AppPermissions.TimeSheetsSubmit)));
    options.AddPolicy("MasterOrAbove", policy =>
        policy.Requirements.Add(new PermissionRequirement(AppPermissions.TimeSheetsApprove)));
    options.AddPolicy("ProductionOrAdmin", policy =>
        policy.Requirements.Add(new PermissionRequirement(AppPermissions.ProductionView)));
    options.AddPolicy("EngineeringAccess", policy =>
        policy.Requirements.Add(new PermissionRequirement(AppPermissions.EngineeringView)));
    options.AddPolicy("AdministrationAccess", policy =>
        policy.Requirements.Add(new PermissionRequirement(AppPermissions.AdministrationAccess)));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ILoggerProvider, DatabaseLoggerProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddScoped<IAccessService, AccessService>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<NotificationPreferenceService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<SavedFilterService>();
builder.Services.AddScoped<PersonalDashboardService>();
builder.Services.AddScoped<BulkTimeSheetApprovalService>();
builder.Services.AddScoped<MissingTimeSheetNotificationService>();
builder.Services.AddHostedService<DailyMissingTimeSheetWorker>();
builder.Services.AddScoped<EngineeringTaskReminderService>();
builder.Services.AddHostedService<EngineeringTaskReminderWorker>();
builder.Services.AddScoped<EngineeringTaskService>();
builder.Services.AddScoped<TelegramLinkService>();
builder.Services.AddScoped<EngineeringTaskAttachmentService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<ExcelService>();

builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));
var telegramToken = builder.Configuration["Telegram:BotToken"];
if (!string.IsNullOrWhiteSpace(telegramToken))
    builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(telegramToken));
builder.Services.AddSingleton<ITelegramBotApi, TelegramBotApi>();
builder.Services.AddSingleton<TelegramConversationStore>();
builder.Services.AddScoped<ITelegramNotificationSender, TelegramNotificationSender>();
builder.Services.AddScoped<TelegramUpdateHandler>();
builder.Services.AddHostedService<TelegramBotWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<WorkTimeTracker.Middleware.RequestLoggingMiddleware>();
app.UseAuthorization();
app.MapRazorPages();
app.MapGet("/", context =>
{
    context.Response.Redirect("/Index");
    return Task.CompletedTask;
});

app.Run();
