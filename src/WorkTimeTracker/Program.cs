using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

var builder = WebApplication.CreateBuilder(args);

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
    .AddDefaultTokenProviders();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Employee", AppRoles.Employee);
    options.Conventions.AuthorizeFolder("/Master", "MasterOrAbove");
    options.Conventions.AuthorizeFolder("/Production", AppRoles.ProductionHead);
    options.Conventions.AuthorizeFolder("/Admin", AppRoles.Admin);
    // Регистрация новых учётных записей — только для администратора.
    options.Conventions.AuthorizeAreaPage("Identity", "/Account/Register", AppRoles.Admin);
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
    options.AddPolicy("MasterOrAbove", policy =>
        policy.RequireRole(AppRoles.SectionMaster, AppRoles.ProductionHead, AppRoles.Admin));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<ExcelService>();

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
app.UseAuthorization();
app.MapRazorPages();
app.MapGet("/", context =>
{
    context.Response.Redirect("/Index");
    return Task.CompletedTask;
});

app.Run();
