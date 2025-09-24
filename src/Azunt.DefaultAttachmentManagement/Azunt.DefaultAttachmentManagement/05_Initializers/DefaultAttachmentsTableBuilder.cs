using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Azunt.DefaultAttachmentManagement;

public class DefaultAttachmentsTableBuilder
{
    private readonly string _masterConnectionString;
    private readonly ILogger<DefaultAttachmentsTableBuilder> _logger;
    private readonly bool _enableSeeding;

    public DefaultAttachmentsTableBuilder(
        string masterConnectionString,
        ILogger<DefaultAttachmentsTableBuilder> logger,
        bool enableSeeding = true)
    {
        _masterConnectionString = masterConnectionString;
        _logger = logger;
        _enableSeeding = enableSeeding;
    }

    public void BuildTenantDatabases()
    {
        var tenantConnectionStrings = GetTenantConnectionStrings();

        foreach (var connStr in tenantConnectionStrings)
        {
            try
            {
                EnsureDefaultAttachmentsTable(connStr);
                _logger.LogInformation("DefaultAttachments table processed for tenant database.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tenant database.");
            }
        }
    }

    public void BuildMasterDatabase()
    {
        try
        {
            EnsureDefaultAttachmentsTable(_masterConnectionString);
            _logger.LogInformation("DefaultAttachments table processed for master database.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing master database.");
        }
    }

    private List<string> GetTenantConnectionStrings()
    {
        var result = new List<string>();

        using var connection = new SqlConnection(_masterConnectionString);
        connection.Open();

        using var cmd = new SqlCommand("SELECT ConnectionString FROM dbo.Tenants", connection);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var connectionString = reader["ConnectionString"]?.ToString();
            if (!string.IsNullOrEmpty(connectionString))
            {
                result.Add(connectionString);
            }
        }

        return result;
    }

    private void EnsureDefaultAttachmentsTable(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();

        // 1) 테이블 존재 여부 확인
        using (var cmdCheck = new SqlCommand(@"
            SELECT COUNT(*) 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'DefaultAttachments';", connection))
        {
            int tableCount = (int)cmdCheck.ExecuteScalar();

            if (tableCount == 0)
            {
                // 2) 신규 생성 (스키마는 요청하신 정의와 동일)
                using var cmdCreate = new SqlCommand(@"
                    CREATE TABLE [dbo].[DefaultAttachments]
                    (
                        [Id]            BIGINT IDENTITY (1, 1) NOT NULL PRIMARY KEY,
                        [Active]        BIT             NULL CONSTRAINT DF_DefaultAttachments_Active DEFAULT (1),
                        [CreatedAt]     DATETIMEOFFSET  NULL CONSTRAINT DF_DefaultAttachments_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
                        [CreatedBy]     NVARCHAR(255)   NULL,
                        [Name]          NVARCHAR(MAX)   NULL,
                        [ApplicantType] INT             NULL CONSTRAINT DF_DefaultAttachments_ApplicantType DEFAULT (0),
                        [Type]          NVARCHAR(255)   NULL,
                        [IsRequired]    BIT             NULL CONSTRAINT DF_DefaultAttachments_IsRequired DEFAULT (1)
                    );", connection);

                cmdCreate.ExecuteNonQuery();
                _logger.LogInformation("DefaultAttachments table created.");
            }
            else
            {
                // 3) 누락된 컬럼만 추가 (타입/기본값 정의 포함)
                var expectedColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    // ColumnName -> Full type clause used in ALTER TABLE ADD
                    ["Active"] = "BIT NULL CONSTRAINT DF_DefaultAttachments_Active DEFAULT (1)",
                    ["CreatedAt"] = "DATETIMEOFFSET NULL CONSTRAINT DF_DefaultAttachments_CreatedAt DEFAULT SYSDATETIMEOFFSET()",
                    ["CreatedBy"] = "NVARCHAR(255) NULL",
                    ["Name"] = "NVARCHAR(MAX) NULL",
                    ["ApplicantType"] = "INT NULL CONSTRAINT DF_DefaultAttachments_ApplicantType DEFAULT (0)",
                    ["Type"] = "NVARCHAR(255) NULL",
                    ["IsRequired"] = "BIT NULL CONSTRAINT DF_DefaultAttachments_IsRequired DEFAULT (1)"
                };

                foreach (var (columnName, typeClause) in expectedColumns)
                {
                    AddColumnIfMissing(connection, "dbo", "DefaultAttachments", columnName, typeClause);
                }
            }
        }

        // 4) 초기 데이터 삽입 (옵션)
        if (_enableSeeding)
        {
            using var cmdCountRows = new SqlCommand("SELECT COUNT(*) FROM [dbo].[DefaultAttachments];", connection);
            int rowCount = (int)cmdCountRows.ExecuteScalar();

            if (rowCount == 0)
            {
                // ApplicantType 예시: 0=기본/미정, 1=Vendor, 2=Employee
                using var cmdInsertDefaults = new SqlCommand(@"
                    INSERT INTO [dbo].[DefaultAttachments] (Active, CreatedAt, CreatedBy, Name, ApplicantType, [Type], IsRequired)
                    VALUES
                        (1, SYSDATETIMEOFFSET(), N'System', N'사업자등록증 사본',       1, N'Document', 1),
                        (1, SYSDATETIMEOFFSET(), N'System', N'신분증 사본',             2, N'Document', 1),
                        (1, SYSDATETIMEOFFSET(), N'System', N'기타 참고자료(선택)',     0, N'Etc',      0);", connection);

                int inserted = cmdInsertDefaults.ExecuteNonQuery();
                _logger.LogInformation("DefaultAttachments seed inserted: {Count}", inserted);
            }
        }
    }

    private static void AddColumnIfMissing(SqlConnection connection, string schema, string table, string column, string typeClause)
    {
        if (!ColumnExists(connection, schema, table, column))
        {
            using var alterCmd = new SqlCommand(
                $"ALTER TABLE [{schema}].[{table}] ADD [{column}] {typeClause};", connection);
            alterCmd.ExecuteNonQuery();
        }
    }

    private static bool ColumnExists(SqlConnection connection, string schema, string table, string column)
    {
        using var cmd = new SqlCommand(@"
            SELECT COUNT(*) 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table AND COLUMN_NAME = @Column;", connection);

        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@Table", table);
        cmd.Parameters.AddWithValue("@Column", column);

        return (int)cmd.ExecuteScalar() > 0;
    }

    public static void Run(IServiceProvider services, bool forMaster, bool enableSeeding = true)
    {
        try
        {
            var logger = services.GetRequiredService<ILogger<DefaultAttachmentsTableBuilder>>();
            var config = services.GetRequiredService<IConfiguration>();
            var masterConnectionString = config.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(masterConnectionString))
                throw new InvalidOperationException("DefaultConnection is not configured in appsettings.json.");

            var builder = new DefaultAttachmentsTableBuilder(masterConnectionString, logger, enableSeeding);

            if (forMaster)
                builder.BuildMasterDatabase();
            else
                builder.BuildTenantDatabases();
        }
        catch (Exception ex)
        {
            var fallbackLogger = services.GetService<ILogger<DefaultAttachmentsTableBuilder>>();
            fallbackLogger?.LogError(ex, "Error while processing DefaultAttachments table.");
        }
    }
}