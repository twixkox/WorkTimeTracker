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
        var configuration = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var isDevelopment = environment.IsDevelopment();

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

        Department Dept(string name, bool weekly = false, bool timeOnly = false) =>
            db.Departments.Local.FirstOrDefault(d => d.Name == name)
            ?? db.Departments.FirstOrDefault(d => d.Name == name)
            ?? new Department { Name = name, IsWeeklyView = weekly, IsTimeOnly = timeOnly };

        var assembly = Dept("Сборка");
        var carpentry = Dept("Столярка");
        var sewing = Dept("Швейка");
        var dept15 = Dept("15", weekly: true);
        var warehouse = Dept("Склад", timeOnly: true);
        var driver = Dept("Водитель", timeOnly: true);
        var painting = Dept("Малярка");
        var welding = Dept("Сварка");
        foreach (var d in new[] { assembly, carpentry, sewing, dept15, warehouse, driver, painting, welding })
            if (d.Id == 0) db.Departments.Add(d);
        await db.SaveChangesAsync();

        // --- Администратор ---
        var adminEmail = configuration["SeedAdmin:Email"] ?? "admin@worktime.local";
        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var adminPassword = configuration["SeedAdmin:Password"];
            if (string.IsNullOrWhiteSpace(adminPassword))
            {
                if (!isDevelopment)
                    throw new InvalidOperationException("Для первого запуска задайте SeedAdmin:Password (не короче 12 символов).");
                adminPassword = "Admin@123456";
            }
            if (!isDevelopment && adminPassword.Length < 12)
                throw new InvalidOperationException("Пароль первого администратора должен содержать не менее 12 символов.");

            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                DisplayName = "Администратор",
                IsActive = true,
            };
            var result = await userManager.CreateAsync(admin, adminPassword);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Не удалось создать администратора: {string.Join("; ", result.Errors.Select(e => e.Description))}");
            await userManager.AddToRoleAsync(admin, AppRoles.Admin);
        }

        // --- Сотрудники и учётные записи ---
        async Task EnsureUserAsync(string email, string fullName, string password, string role, Employee employee)
        {
            if (!isDevelopment) return;

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

        // По пять активных человек на участок, включая мастера и руководителя.
        // Старые лишние демо-записи деактивируются, чтобы сохранить табели и учётные записи.
        async Task EnsureWorkersAsync(Department dept, string position, string[] names, int firstUserNumber, int workersCount = 5)
        {
            for (var i = 0; i < names.Length; i++)
            {
                if (i < workersCount)
                {
                    var employee = await EnsureEmployeeAsync(names[i], dept, position);
                    if (!employee.IsActive) employee.IsActive = true;
                    await EnsureUserAsync($"worker{firstUserNumber + i}@worktime.local", names[i], "Worker@123456", AppRoles.Employee, employee);
                }
                else
                {
                    var legacy = await db.Employees.FirstOrDefaultAsync(e =>
                        e.FullName == names[i] && e.DepartmentId == dept.Id && e.Position == position);
                    if (legacy is not null) legacy.IsActive = false;
                }
            }
            await db.SaveChangesAsync();
        }

        await EnsureWorkersAsync(assembly, "Сборщик", new[]
        {
            "Иванов А.А.", "Петров Б.Б.", "Сидоров В.В.", "Кузнецов Г.Г.", "Смирнов Д.Д.",
            "Попов Е.Е.", "Волков Ж.Ж.", "Соколов З.З.", "Лебедев И.И.", "Козлов К.К.",
            "Новиков Л.Л.", "Морозов М.М.", "Павлов Н.Н.", "Семёнов О.О.", "Голубев П.П.",
            "Виноградов Р.Р.", "Богданов С.С.",
        }, 1, workersCount: 3);
        await EnsureWorkersAsync(carpentry, "Столяр", new[]
        {
            "Степанов Т.Т.", "Егоров У.У.", "Орлов Ф.Ф.", "Захаров Х.Х.", "Фролов Л.Л.",
        }, 18, workersCount: 4);
        await EnsureWorkersAsync(sewing, "Швея", new[]
        {
            "Андреев Ц.Ц.", "Макаров Ч.Ч.", "Николаев Ш.Ш.", "Захарченко Щ.Щ.", "Романова Е.Е.",
        }, 23, workersCount: 4);
        await EnsureWorkersAsync(dept15, "Рабочий", new[]
        {
            "Киселёв Ю.Ю.", "Титов Я.Я.", "Ковалёв Э.Э.", "Митин Ъ.Ъ.", "Денисов Ь.Ь.",
            "Громов А.Б.", "Фролов В.Г", "Жуков Г.Д.", "Матвеев Д.Е.", "Белов Е.Ж",
            "Комаров Ж.З", "Давыдов З.И.", "Мельников И.К.",
        }, 28, workersCount: 4);
        await EnsureWorkersAsync(warehouse, "Кладовщик", new[]
        {
            "Васильев А.И.", "Фёдоров Б.И.", "Михайлов В.И.", "Ефремов Г.И.", "Дмитриев Д.И.",
        }, 39);
        await EnsureWorkersAsync(driver, "Водитель", new[]
        {
            "Орлов А.В.", "Беляев Б.В.", "Тихонов В.В.", "Гусев Г.В.", "Куликов Д.В.",
        }, 44);
        await EnsureWorkersAsync(painting, "Маляр", new[]
        {
            "Маркин А.С.", "Лазарев Б.С.", "Власов В.С.", "Королёв Г.С.", "Савин Д.С.",
        }, 49);
        await EnsureWorkersAsync(welding, "Сварщик", new[]
        {
            "Карпов А.Н.", "Баранов Б.Н.", "Герасимов В.Н.", "Лебедев Г.Н.", "Терентьев Д.Н.",
        }, 54);

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
        await EnsureSettingAsync(SettingKeys.ShiftStart, ShiftDefaults.Start.ToString("HH:mm"));
        await EnsureSettingAsync(SettingKeys.ShiftEnd, ShiftDefaults.End.ToString("HH:mm"));
        await EnsureSettingAsync(SettingKeys.CheckIntervalOverlap, "false");

        var shiftStart = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingKeys.ShiftStart);
        var shiftEnd = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingKeys.ShiftEnd);
        if (shiftStart?.Value == "08:00" && shiftEnd?.Value == "16:30")
        {
            shiftStart.Value = ShiftDefaults.Start.ToString("HH:mm");
            shiftEnd.Value = ShiftDefaults.End.ToString("HH:mm");
        }
        await db.SaveChangesAsync();
    }
}
