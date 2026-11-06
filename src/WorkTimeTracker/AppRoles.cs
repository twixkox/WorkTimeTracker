namespace WorkTimeTracker;

/// <summary>Роли приложения.</summary>
public static class AppRoles
{
    public const string Employee = "Employee";
    public const string SectionMaster = "SectionMaster";
    public const string ProductionHead = "ProductionHead";
    public const string Admin = "Admin";

    public static readonly string[] All = { Employee, SectionMaster, ProductionHead, Admin };
}

/// <summary>Ключи настроек системы.</summary>
public static class SettingKeys
{
    public const string MaxHoursPerDay = "MaxHoursPerDay";
    public const string OvertimeThreshold = "OvertimeThreshold";
    public const string ShiftStart = "ShiftStart";
    public const string ShiftEnd = "ShiftEnd";
    public const string CheckIntervalOverlap = "CheckIntervalOverlap";
}
