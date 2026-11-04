using ClosedXML.Excel;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

/// <summary>Экспорт и импорт данных в Excel (ClosedXML).</summary>
public class ExcelService
{
    private static void StyleHeader(IXLRangeRow row)
    {
        row.Style.Font.Bold = true;
        row.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E2F3");
        row.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
    }

    /// <summary>Табель участка за период: лист с детализацией + лист сводки по катерам.</summary>
    public byte[] ExportSectionTimesheet(Department department, DateOnly from, DateOnly to,
        IReadOnlyList<(Employee Employee, List<WorkDay> Days)> data, List<BoatSummaryRow> boatSummary)
    {
        using var wb = new XLWorkbook();

        var ws = wb.Worksheets.Add("Табель");
        ws.Cell(1, 1).Value = $"Табель участка «{department.Name}» за период {from:dd.MM.yyyy} – {to:dd.MM.yyyy}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        var headers = new[] { "Сотрудник", "Дата", "Начало", "Окончание", "Отработано, ч", "№ катера", "Время затрат, ч", "Выполненные работы", "Внутрицеховые", "Статус" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(3, i + 1).Value = headers[i];
        StyleHeader(ws.Range(3, 1, 3, headers.Length).FirstRow());

        var row = 4;
        foreach (var (employee, days) in data)
        {
            foreach (var day in days)
            {
                var entries = day.TimeEntries.DefaultIfEmpty();
                foreach (var entry in entries)
                {
                    ws.Cell(row, 1).Value = employee.FullName;
                    ws.Cell(row, 2).Value = day.Date.ToString("dd.MM.yyyy");
                    ws.Cell(row, 3).Value = day.StartTime.ToString("HH:mm");
                    ws.Cell(row, 4).Value = day.EndTime.ToString("HH:mm");
                    ws.Cell(row, 5).Value = day.WorkedHours;
                    if (entry is not null)
                    {
                        if (entry.IsInhouse)
                            ws.Cell(row, 9).Value = "да";
                        else
                            ws.Cell(row, 6).Value = entry.Boat?.Number ?? string.Empty;
                        ws.Cell(row, 7).Value = entry.Hours;
                        ws.Cell(row, 8).Value = entry.WorkDescription;
                    }
                    ws.Cell(row, 10).Value = StatusName(day.Status);
                    row++;
                }
            }
        }
        ws.Columns().AdjustToContents();

        AddBoatSummarySheet(wb, boatSummary);
        return Save(wb);
    }

    /// <summary>Табель сотрудника за месяц.</summary>
    public byte[] ExportEmployeeTimesheet(Employee employee, DateOnly from, DateOnly to, IReadOnlyList<WorkDay> days)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Табель");
        ws.Cell(1, 1).Value = $"{employee.FullName} — табель за период {from:dd.MM.yyyy} – {to:dd.MM.yyyy}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        var headers = new[] { "Дата", "Начало", "Окончание", "Отработано, ч", "№ катера", "Время затрат, ч", "Выполненные работы", "Внутрицеховые", "Статус" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(3, i + 1).Value = headers[i];
        StyleHeader(ws.Range(3, 1, 3, headers.Length).FirstRow());

        var row = 4;
        foreach (var day in days)
        {
            foreach (var entry in day.TimeEntries.DefaultIfEmpty())
            {
                ws.Cell(row, 1).Value = day.Date.ToString("dd.MM.yyyy");
                ws.Cell(row, 2).Value = day.StartTime.ToString("HH:mm");
                ws.Cell(row, 3).Value = day.EndTime.ToString("HH:mm");
                ws.Cell(row, 4).Value = day.WorkedHours;
                if (entry is not null)
                {
                    if (entry.IsInhouse)
                        ws.Cell(row, 8).Value = "да";
                    else
                        ws.Cell(row, 5).Value = entry.Boat?.Number ?? string.Empty;
                    ws.Cell(row, 6).Value = entry.Hours;
                    ws.Cell(row, 7).Value = entry.WorkDescription;
                }
                ws.Cell(row, 9).Value = StatusName(day.Status);
                row++;
            }
        }
        ws.Cell(row + 1, 3).Value = "Итого отработано:";
        ws.Cell(row + 1, 4).Value = days.Sum(d => d.WorkedHours);
        ws.Cell(row + 1, 3).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
        return Save(wb);
    }

    public byte[] ExportBoatSummary(DateOnly from, DateOnly to, List<BoatSummaryRow> rows, string? departmentName = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Сводка по катерам");
        ws.Cell(1, 1).Value = $"Трудозатраты по катерам ({departmentName ?? "все участки"}) за {from:dd.MM.yyyy} – {to:dd.MM.yyyy}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        var headers = new[] { "№ катера", "Название", "Заказчик", "Сумма часов", "Кол-во записей" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(3, i + 1).Value = headers[i];
        StyleHeader(ws.Range(3, 1, 3, headers.Length).FirstRow());

        var r = 4;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.BoatNumber;
            ws.Cell(r, 2).Value = row.BoatName;
            ws.Cell(r, 3).Value = row.Customer;
            ws.Cell(r, 4).Value = row.Hours;
            ws.Cell(r, 5).Value = row.EntriesCount;
            r++;
        }
        ws.Cell(r + 1, 3).Value = "Итого:";
        ws.Cell(r + 1, 4).Value = rows.Sum(x => x.Hours);
        ws.Cell(r + 1, 3).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
        return Save(wb);
    }

    public byte[] ExportEmployeeLoad(DateOnly from, DateOnly to, List<EmployeeLoadRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Загрузка сотрудников");
        ws.Cell(1, 1).Value = $"Загрузка сотрудников за {from:dd.MM.yyyy} – {to:dd.MM.yyyy}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        var headers = new[] { "Сотрудник", "Подразделение", "Отработано, ч", "По катерам, ч", "Внутрицеховые, ч", "Дней заполнено", "Дней утверждено" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(3, i + 1).Value = headers[i];
        StyleHeader(ws.Range(3, 1, 3, headers.Length).FirstRow());

        var r = 4;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.FullName;
            ws.Cell(r, 2).Value = row.DepartmentName;
            ws.Cell(r, 3).Value = row.WorkedHours;
            ws.Cell(r, 4).Value = row.BoatHours;
            ws.Cell(r, 5).Value = row.InhouseHours;
            ws.Cell(r, 6).Value = row.DaysFilled;
            ws.Cell(r, 7).Value = row.DaysApproved;
            r++;
        }
        ws.Columns().AdjustToContents();
        return Save(wb);
    }

    private void AddBoatSummarySheet(XLWorkbook wb, List<BoatSummaryRow> rows)
    {
        var ws = wb.Worksheets.Add("Сводка по катерам");
        var headers = new[] { "№ катера", "Название", "Заказчик", "Сумма часов" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        StyleHeader(ws.Range(1, 1, 1, headers.Length).FirstRow());
        var r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.BoatNumber;
            ws.Cell(r, 2).Value = row.BoatName;
            ws.Cell(r, 3).Value = row.Customer;
            ws.Cell(r, 4).Value = row.Hours;
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    /// <summary>Импорт катеров из Excel: колонки «№ катера», «Название», «Заказчик».</summary>
    public List<(string Number, string? Name, string? Customer)> ImportBoats(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var result = new List<(string, string?, string?)>();
        foreach (var row in ws.RowsUsed().Skip(1)) // пропуск заголовка
        {
            var number = row.Cell(1).GetString().Trim();
            if (string.IsNullOrEmpty(number)) continue;
            var name = row.Cell(2).GetString().Trim();
            var customer = row.Cell(3).GetString().Trim();
            result.Add((number, string.IsNullOrEmpty(name) ? null : name, string.IsNullOrEmpty(customer) ? null : customer));
        }
        return result;
    }

    /// <summary>Импорт сотрудников из Excel: колонки «ФИО», «Подразделение», «Должность».</summary>
    public List<(string FullName, string DepartmentName, string? Position)> ImportEmployees(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var result = new List<(string, string, string?)>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var fullName = row.Cell(1).GetString().Trim();
            var dept = row.Cell(2).GetString().Trim();
            if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(dept)) continue;
            var position = row.Cell(3).GetString().Trim();
            result.Add((fullName, dept, string.IsNullOrEmpty(position) ? null : position));
        }
        return result;
    }

    private static byte[] Save(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string StatusName(TimeSheetStatus status) => status switch
    {
        TimeSheetStatus.Draft => "Черновик",
        TimeSheetStatus.Submitted => "На утверждении",
        TimeSheetStatus.Approved => "Утверждён",
        TimeSheetStatus.Rejected => "Отклонён",
        _ => status.ToString(),
    };
}
