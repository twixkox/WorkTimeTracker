using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

/// <summary>Начальное заполнение БД: роли, админ, подразделения, сотрудники, катера, настройки.</summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<ApplicationDbContext>();

        await db.Database.MigrateAsync();

        foreach (var role in AppRoles.All)
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        // --- Подразделения ---
        // Переименование старых названий в актуальные (миграция данных).
        var renameMap = new Dictionary<string, (string Name, bool Weekly)>
        {
            ["Сбор"] = ("Сборка", false),
            ["Столяр"] = ("Столярка", false),
        };
        foreach (var (oldName, (newName, weekly)) in renameMap)
        {
            var legacy = await db.Departments.FirstOrDefaultAsync(d => d.Name == oldName);
            if (legacy is not null)
            {
                legacy.Name = newName;
                legacy.IsWeeklyView = weekly;
            }
        }
        await db.SaveChangesAsync();

        Department Dept(string name, bool weekly = false) =>
            db.Departments.Local.FirstOrDefault(d => d.Name == name)
            ?? db.Departments.FirstOrDefault(d => d.Name == name)
            ?? new Department { Name = name, IsWeeklyView = weekly };

        var assembly = Dept("Сборка");
        var carpentry = Dept("Столярка");
        var sewing = Dept("Швейка");
        var dept15 = Dept("15", weekly: true);
        foreach (var d in new[] { assembly, carpentry, sewing, dept15 })
            if (d.Id == 0) db.Departments.Add(d);
        await db.SaveChangesAsync();

        // --- Администратор ---
        const string adminEmail = "admin@worktime.local";
        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                DisplayName = "Администратор",
                IsActive = true,
            };
            await userManager.CreateAsync(admin, "Admin@123456");
            await userManager.AddToRoleAsync(admin, AppRoles.Admin);
        }

        // --- Сотрудники и учётные записи ---
        async Task EnsureUserAsync(string email, string fullName, string password, string role, Employee employee)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = fullName,
                    IsActive = true,
                };
                await userManager.CreateAsync(user, password);
                await userManager.AddToRoleAsync(user, role);
            }
            if (employee.UserId is null)
            {
                employee.UserId = user.Id;
                await db.SaveChangesAsync();
            }
        }

        async Task<Employee> EnsureEmployeeAsync(string fullName, Department dept, string? position = null)
        {
            var e = await db.Employees.FirstOrDefaultAsync(x => x.FullName == fullName && x.DepartmentId == dept.Id);
            if (e is null)
            {
                e = new Employee { FullName = fullName, DepartmentId = dept.Id, Position = position };
                db.Employees.Add(e);
                await db.SaveChangesAsync();
            }
            return e;
        }

        // Состав согласно заданию (ФИО демонстрационные, легко заменить через интерфейс администратора).
        var assemblyNames = new[]
        {
            "Иванов А.А.", "Петров Б.Б.", "Сидоров В.В.", "Кузнецов Г.Г.", "Смирнов Д.Д.",
            "Попов Е.Е.", "Волков Ж.Ж.", "Соколов З.З.", "Лебедев И.И.", "Козлов К.К.",
            "Новиков Л.Л.", "Морозов М.М.", "Павлов Н.Н.", "Семёнов О.О.", "Голубев П.П.",
            "Виноградов Р.Р.", "Богданов С.С.",
        };
        var carpentryNames = new[] { "Степанов Т.Т.", "Егоров У.У.", "Орлов Ф.Ф.", "Захаров Х.Х." };
        var sewingNames = new[] { "Андреев Ц.Ц.", "Макаров Ч.Ч.", "Николаев Ш.Ш.", "Захарченко Щ.Щ." };
        var dept15Names = new[]
        {
            "Киселёв Ю.Ю.", "Титов Я.Я.", "Ковалёв Э.Э.", "Митин Ъ.Ъ.", "Денисов Ь.Ь.",
            "Громов А.Б.", "Фролов В.Г", "Жуков Г.Д.", "Матвеев Д.Е.", "Белов Е.Ж",
            "Комаров Ж.З", "Давыдов З.И.", "Мельников И.К.",
        };

        var password = "Worker@123456";
        var idx = 1;
        foreach (var name in assemblyNames)
        {
            var e = await EnsureEmployeeAsync(name, assembly, "Сборщик");
            await EnsureUserAsync($"worker{idx++}@worktime.local", name, password, AppRoles.Employee, e);
        }
        foreach (var name in carpentryNames)
        {
            var e = await EnsureEmployeeAsync(name, carpentry, "Столяр");
            await EnsureUserAsync($"worker{idx++}@worktime.local", name, password, AppRoles.Employee, e);
        }
        foreach (var name in sewingNames)
        {
            var e = await EnsureEmployeeAsync(name, sewing, "Швея");
            await EnsureUserAsync($"worker{idx++}@worktime.local", name, password, AppRoles.Employee, e);
        }
        foreach (var name in dept15Names)
        {
            var e = await EnsureEmployeeAsync(name, dept15, "Рабочий");
            await EnsureUserAsync($"worker{idx++}@worktime.local", name, password, AppRoles.Employee, e);
        }

        // Мастера участков и начальник производства
        async Task EnsureMasterAsync(string email, string fullName, Department dept)
        {
            var e = await EnsureEmployeeAsync(fullName, dept, "Мастер участка");
            await EnsureUserAsync(email, fullName, "Master@123456", AppRoles.SectionMaster, e);
        }
        await EnsureMasterAsync("master.sborka@worktime.local", "Сорокин М.С.", assembly);
        await EnsureMasterAsync("master.stolyar@worktime.local", "Ларин М.Л.", carpentry);
        await EnsureMasterAsync("master.shveyka@worktime.local", "Крылова М.К.", sewing);
        await EnsureMasterAsync("master.15@worktime.local", "Тарасов М.Т.", dept15);

        var headEmployee = await EnsureEmployeeAsync("Генералов Н.П.", assembly, "Начальник производства");
        await EnsureUserAsync("head@worktime.local", "Генералов Н.П.", "Head@123456", AppRoles.ProductionHead, headEmployee);

        // --- Катера (демонстрационные номера) ---
        if (!await db.Boats.AnyAsync())
        {
            for (var i = 1; i <= 22; i++)
                db.Boats.Add(new Boat { Number = i.ToString(), Name = $"Катер {i}" });
            await db.SaveChangesAsync();
        }

        // --- Виды работ ---
        if (!await db.WorkTypes.AnyAsync())
        {
            var workTypes = new[]
            {
                "Сборка корпуса", "Обшивка", "Установка двигателя", "Электромонтаж",
                "Отделка салона", "Покраска", "Шлифовка", "Упаковка", "Контроль качества",
            };
            foreach (var wt in workTypes)
                db.WorkTypes.Add(new WorkType { Name = wt });
            await db.SaveChangesAsync();
        }

        // --- Настройки по умолчанию ---
        async Task EnsureSettingAsync(string key, string value)
        {
            if (!await db.SystemSettings.AnyAsync(s => s.Key == key))
                db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        }
        await EnsureSettingAsync(SettingKeys.MaxHoursPerDay, "16");
        await EnsureSettingAsync(SettingKeys.OvertimeThreshold, "8");
        await EnsureSettingAsync(SettingKeys.TwoStageApproval, "false");
        await EnsureSettingAsync(SettingKeys.ShiftStart, "08:00");
        await EnsureSettingAsync(SettingKeys.ShiftEnd, "16:30");
        await EnsureSettingAsync(SettingKeys.CheckIntervalOverlap, "false");
        await db.SaveChangesAsync();
    }
}
