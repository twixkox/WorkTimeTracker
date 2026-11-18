namespace WorkTimeTracker;

/// <summary>Русские названия ролей для отображения.</summary>
public static class RoleName
{
    public static string ToRussian(string role) => role switch
    {
        AppRoles.Employee => "Сотрудник",
        AppRoles.SectionMaster => "Мастер участка",
        AppRoles.ProductionHead => "Начальник производства",
        AppRoles.ChiefEngineer => "Главный инженер",
        AppRoles.Engineer => "Инженер",
        AppRoles.Admin => "Администратор",
        _ => role,
    };

    public static string FromRussian(string russian) => russian switch
    {
        "Сотрудник" => AppRoles.Employee,
        "Мастер участка" => AppRoles.SectionMaster,
        "Начальник производства" => AppRoles.ProductionHead,
        "Главный инженер" => AppRoles.ChiefEngineer,
        "Инженер" => AppRoles.Engineer,
        "Администратор" => AppRoles.Admin,
        _ => russian,
    };
}
