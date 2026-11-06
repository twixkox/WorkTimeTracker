-- ============================================================================
-- WorkTimeTracker: создание базы данных и начальное заполнение справочников
-- Microsoft SQL Server
--
-- Порядок развёртывания:
--   1. Настройте строку подключения к MSSQL (см. README, раздел «Microsoft SQL Server»).
--   2. Примените миграции EF Core — они создадут БД и схему:
--        dotnet ef database update --project src/WorkTimeTracker
--   3. Выполните этот скрипт — он заполнит справочники (подразделения, катера,
--      виды работ, настройки). При повторном запуске безопасен (проверяет дубликаты).
--   4. Запустите приложение: seeder при первом старте создаст роли, учётную запись
--      администратора, демонстрационных сотрудников и мастеров участков.
-- ============================================================================

IF DB_ID(N'WorkTimeTracker') IS NULL
BEGIN
    CREATE DATABASE [WorkTimeTracker];
END
GO

USE [WorkTimeTracker];
GO

-- Подразделения (участки). IsWeeklyView = 1 — недельный вид табеля, как у «15».
SET IDENTITY_INSERT [dbo].[Departments] ON;
INSERT INTO [dbo].[Departments] ([Id], [Name], [IsWeeklyView], [IsActive]) VALUES
    (1, N'Сборка',  0, 1),
    (2, N'Столярка', 0, 1),
    (3, N'Швейка',   0, 1),
    (4, N'15',       1, 1);
SET IDENTITY_INSERT [dbo].[Departments] OFF;
GO

-- Катера
INSERT INTO [dbo].[Boats] ([Number], [Name], [TrailerNumber], [BoardNumber], [Customer], [IsActive])
SELECT CAST(n AS nvarchar(10)), N'Катер ' + CAST(n AS nvarchar(10)), NULL, NULL, NULL, 1
FROM (SELECT 1 n UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5
      UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9 UNION ALL SELECT 10
      UNION ALL SELECT 11 UNION ALL SELECT 12 UNION ALL SELECT 13 UNION ALL SELECT 14 UNION ALL SELECT 15
      UNION ALL SELECT 16 UNION ALL SELECT 17 UNION ALL SELECT 18 UNION ALL SELECT 19 UNION ALL SELECT 20
      UNION ALL SELECT 21 UNION ALL SELECT 22) t
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Boats]);
GO

-- Виды работ (подсказки для поля «Выполненные работы»)
INSERT INTO [dbo].[WorkTypes] ([Name], [IsActive]) SELECT v, 1 FROM (VALUES
    (N'Сборка корпуса'), (N'Обшивка'), (N'Установка двигателя'), (N'Электромонтаж'),
    (N'Отделка салона'), (N'Покраска'), (N'Шлифовка'), (N'Упаковка'), (N'Контроль качества')
) t(v)
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[WorkTypes] w WHERE w.[Name] = t.v);
GO

-- Настройки системы
MERGE [dbo].[SystemSettings] AS target
USING (VALUES
    (N'MaxHoursPerDay',      N'16'),
    (N'OvertimeThreshold',   N'8'),
    (N'TwoStageApproval',    N'false'),
    (N'ShiftStart',          N'09:00'),
    (N'ShiftEnd',            N'18:00'),
    (N'CheckIntervalOverlap',N'false')
) AS source ([Key], [Value])
ON target.[Key] = source.[Key]
WHEN NOT MATCHED THEN INSERT ([Key], [Value]) VALUES (source.[Key], source.[Value]);
GO

PRINT N'База создана, справочники заполнены. Учётные записи создаст seeder при первом запуске приложения.';
GO
