namespace WorkTimeTracker;

/// <summary>Стабильные коды разрешений, хранимые в утверждениях ролей.</summary>
public static class AppPermissions
{
    public const string ClaimType = "permission";

    public const string TimeSheetsView = "timesheets.view";
    public const string TimeSheetsEdit = "timesheets.edit";
    public const string TimeSheetsSubmit = "timesheets.submit";
    public const string TimeSheetsApprove = "timesheets.approve";

    public const string ProductionView = "production.view";

    public const string EngineeringView = "engineering.view";
    public const string EngineeringCreate = "engineering.create";
    public const string EngineeringEdit = "engineering.edit";
    public const string EngineeringExecute = "engineering.execute";

    public const string ReportsView = "reports.view";
    public const string AdministrationAccess = "administration.access";
    public const string NotificationsView = "notifications.view";
    public const string TelegramUse = "telegram.use";

    public static readonly string[] All =
    {
        TimeSheetsView,
        TimeSheetsEdit,
        TimeSheetsSubmit,
        TimeSheetsApprove,
        ProductionView,
        EngineeringView,
        EngineeringCreate,
        EngineeringEdit,
        EngineeringExecute,
        ReportsView,
        AdministrationAccess,
        NotificationsView,
        TelegramUse,
    };

    private static readonly string[] EmployeePermissions =
    {
        TimeSheetsView,
        TimeSheetsEdit,
        TimeSheetsSubmit,
        NotificationsView,
        TelegramUse,
    };

    private static readonly string[] SectionMasterPermissions =
    {
        TimeSheetsView,
        TimeSheetsEdit,
        TimeSheetsSubmit,
        TimeSheetsApprove,
        ReportsView,
        NotificationsView,
        TelegramUse,
    };

    private static readonly string[] ProductionHeadPermissions =
    {
        TimeSheetsView,
        TimeSheetsEdit,
        TimeSheetsApprove,
        ProductionView,
        EngineeringView,
        EngineeringCreate,
        ReportsView,
        NotificationsView,
        TelegramUse,
    };

    private static readonly string[] ChiefEngineerPermissions =
    {
        EngineeringView,
        EngineeringCreate,
        EngineeringEdit,
        NotificationsView,
        TelegramUse,
    };

    private static readonly string[] EngineerPermissions =
    {
        EngineeringView,
        EngineeringExecute,
        NotificationsView,
        TelegramUse,
    };

    private static readonly string[] AdminPermissions =
    {
        TimeSheetsView,
        TimeSheetsEdit,
        TimeSheetsApprove,
        ProductionView,
        EngineeringView,
        EngineeringCreate,
        EngineeringEdit,
        ReportsView,
        AdministrationAccess,
        NotificationsView,
        TelegramUse,
    };

    public static IReadOnlyCollection<string> ForRole(string role) => role switch
    {
        AppRoles.Employee => EmployeePermissions,
        AppRoles.SectionMaster => SectionMasterPermissions,
        AppRoles.ProductionHead => ProductionHeadPermissions,
        AppRoles.ChiefEngineer => ChiefEngineerPermissions,
        AppRoles.Engineer => EngineerPermissions,
        AppRoles.Admin => AdminPermissions,
        _ => Array.Empty<string>(),
    };
}
