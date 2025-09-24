using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using Azunt.Models.Common; // ArticleSet<>
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Azunt.DefaultAttachmentManagement;

public class DefaultAttachmentRepositoryAdoNet : IDefaultAttachmentRepository
{
    private readonly string _defaultConnectionString;
    private readonly ILogger<DefaultAttachmentRepositoryAdoNet> _logger;

    public DefaultAttachmentRepositoryAdoNet(string defaultConnectionString, ILoggerFactory loggerFactory)
    {
        _defaultConnectionString = defaultConnectionString ?? throw new ArgumentNullException(nameof(defaultConnectionString));
        _logger = loggerFactory.CreateLogger<DefaultAttachmentRepositoryAdoNet>();
    }

    private SqlConnection GetConnection(string? connectionString)
        => new SqlConnection(connectionString ?? _defaultConnectionString);

    #region CRUD

    public async Task<DefaultAttachment> AddAsync(DefaultAttachment model, string? connectionString = null)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));

        await using var conn = GetConnection(connectionString);
        await conn.OpenAsync();

        // INSERT 문을 동적으로 구성: null 값(Active/IsRequired/ApplicantType/CreatedAt 등)은 컬럼을 생략하여 DEFAULT 적용
        var cols = new List<string> { "Name", "CreatedBy", "Type" }; // nvarchar(max/255)
        var vals = new List<string> { "@Name", "@CreatedBy", "@Type" };
        var parameters = new List<SqlParameter>
        {
            SqlParam("@Name", SqlDbType.NVarChar, (object?)model.Name ?? DBNull.Value),          // nvarchar(max) => size -1
            SqlParam("@CreatedBy", SqlDbType.NVarChar, (object?)model.CreatedBy ?? DBNull.Value, 255),
            SqlParam("@Type", SqlDbType.NVarChar, (object?)model.Type ?? DBNull.Value, 255),
        };

        // Optional/DEFAULT 대상: null이면 컬럼 생략
        if (model.Active.HasValue)
        {
            cols.Add("Active"); vals.Add("@Active");
            parameters.Add(SqlParam("@Active", SqlDbType.Bit, model.Active.Value));
        }
        if (model.IsRequired.HasValue)
        {
            cols.Add("IsRequired"); vals.Add("@IsRequired");
            parameters.Add(SqlParam("@IsRequired", SqlDbType.Bit, model.IsRequired.Value));
        }
        if (model.ApplicantType.HasValue)
        {
            cols.Add("ApplicantType"); vals.Add("@ApplicantType");
            parameters.Add(SqlParam("@ApplicantType", SqlDbType.Int, model.ApplicantType.Value));
        }
        // CreatedAt은 DB 기본값(SYSDATETIMEOFFSET()) 사용 → 의도적으로 생략

        var sb = new StringBuilder();
        sb.Append("INSERT INTO dbo.DefaultAttachments (")
          .Append(string.Join(", ", cols))
          .Append(") OUTPUT INSERTED.Id VALUES (")
          .Append(string.Join(", ", vals))
          .Append(");");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sb.ToString();
        foreach (var p in parameters) cmd.Parameters.Add(p);

        var result = await cmd.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value)
            throw new InvalidOperationException("Failed to insert DefaultAttachment. No ID was returned.");

        model.Id = Convert.ToInt64(result);
        return model;
    }

    public async Task<List<DefaultAttachment>> GetAllAsync(string? connectionString = null)
    {
        var list = new List<DefaultAttachment>();

        await using var conn = GetConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
SELECT Id, Active, CreatedAt, CreatedBy, Name, ApplicantType, [Type], IsRequired
FROM dbo.DefaultAttachments
ORDER BY Id DESC;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(Map(r));

        return list;
    }

    public async Task<DefaultAttachment> GetByIdAsync(long id, string? connectionString = null)
    {
        await using var conn = GetConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
SELECT Id, Active, CreatedAt, CreatedBy, Name, ApplicantType, [Type], IsRequired
FROM dbo.DefaultAttachments
WHERE Id = @Id;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(SqlParam("@Id", SqlDbType.BigInt, id));

        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
            return Map(r);

        return new DefaultAttachment(); // 컨벤션 유지
    }

    public async Task<bool> UpdateAsync(DefaultAttachment model, string? connectionString = null)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));

        await using var conn = GetConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
UPDATE dbo.DefaultAttachments
   SET Active        = @Active,
       Name          = @Name,
       [Type]        = @Type,
       IsRequired    = @IsRequired,
       CreatedBy     = @CreatedBy,
       ApplicantType = @ApplicantType
 WHERE Id            = @Id;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        cmd.Parameters.Add(SqlParam("@Active", SqlDbType.Bit, (object?)model.Active ?? DBNull.Value));
        cmd.Parameters.Add(SqlParam("@Name", SqlDbType.NVarChar, (object?)model.Name ?? DBNull.Value)); // nvarchar(max)
        cmd.Parameters.Add(SqlParam("@Type", SqlDbType.NVarChar, (object?)model.Type ?? DBNull.Value, 255));
        cmd.Parameters.Add(SqlParam("@IsRequired", SqlDbType.Bit, (object?)model.IsRequired ?? DBNull.Value));
        cmd.Parameters.Add(SqlParam("@CreatedBy", SqlDbType.NVarChar, (object?)model.CreatedBy ?? DBNull.Value, 255));
        cmd.Parameters.Add(SqlParam("@ApplicantType", SqlDbType.Int, (object?)model.ApplicantType ?? DBNull.Value));
        cmd.Parameters.Add(SqlParam("@Id", SqlDbType.BigInt, model.Id));

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteAsync(long id, string? connectionString = null)
    {
        await using var conn = GetConnection(connectionString);
        await conn.OpenAsync();

        const string sql = "DELETE FROM dbo.DefaultAttachments WHERE Id = @Id;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(SqlParam("@Id", SqlDbType.BigInt, id));

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    #endregion

    #region Paged/Search/Sort

    public async Task<ArticleSet<DefaultAttachment, int>> GetAllAsync<TParentIdentifier>(
        int pageIndex,
        int pageSize,
        string searchField, // 현재 미사용(호환용). 필요 시 필드별 검색으로 확장.
        string searchQuery,
        string sortOrder,
        TParentIdentifier parentIdentifier,
        string? connectionString = null)
    {
        if (pageIndex < 0) pageIndex = 0;
        if (pageSize <= 0) pageSize = 10;

        await using var conn = GetConnection(connectionString);
        await conn.OpenAsync();

        // WHERE 빌드
        var where = new StringBuilder();
        var parameters = new List<SqlParameter>();
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var like = $"%{searchQuery}%";
            where.AppendLine("WHERE (")
                 .AppendLine("       (Name      IS NOT NULL AND Name      LIKE @qName)")
                 .AppendLine("    OR (CreatedBy IS NOT NULL AND CreatedBy LIKE @qCreatedBy)")
                 .AppendLine("    OR ([Type]    IS NOT NULL AND [Type]    LIKE @qType)");

            parameters.Add(SqlParam("@qName", SqlDbType.NVarChar, like));
            parameters.Add(SqlParam("@qCreatedBy", SqlDbType.NVarChar, like, 255));
            parameters.Add(SqlParam("@qType", SqlDbType.NVarChar, like, 255));

            if (bool.TryParse(searchQuery, out var boolQuery))
            {
                where.AppendLine("    OR (IsRequired = @qBool) OR (Active = @qBool)");
                parameters.Add(SqlParam("@qBool", SqlDbType.Bit, boolQuery));
            }
            if (int.TryParse(searchQuery, out var intQuery))
            {
                where.AppendLine("    OR (ApplicantType = @qInt)");
                parameters.Add(SqlParam("@qInt", SqlDbType.Int, intQuery));
            }
            where.AppendLine(")");
        }

        // ORDER BY 화이트리스트
        var orderBy = BuildOrderBy(sortOrder);

        // COUNT
        var countSql = $@"
SELECT COUNT(1)
FROM dbo.DefaultAttachments
{where};";

        int totalCount;
        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = countSql;
            foreach (var p in parameters) countCmd.Parameters.Add(Clone(p));
            var scalar = await countCmd.ExecuteScalarAsync();
            totalCount = Convert.ToInt32(scalar);
        }

        // PAGE (0-based pageIndex)
        var offset = pageIndex * pageSize;
        var pageSql = $@"
SELECT Id, Active, CreatedAt, CreatedBy, Name, ApplicantType, [Type], IsRequired
FROM dbo.DefaultAttachments
{where}
{orderBy}
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;";

        var items = new List<DefaultAttachment>();
        await using (var pageCmd = conn.CreateCommand())
        {
            pageCmd.CommandText = pageSql;
            foreach (var p in parameters) pageCmd.Parameters.Add(Clone(p));
            pageCmd.Parameters.Add(SqlParam("@offset", SqlDbType.Int, offset));
            pageCmd.Parameters.Add(SqlParam("@pageSize", SqlDbType.Int, pageSize));

            await using var r = await pageCmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                items.Add(Map(r));
        }

        return new ArticleSet<DefaultAttachment, int>(items, totalCount);
    }

    private static string BuildOrderBy(string sortOrder)
    {
        var key = (sortOrder ?? string.Empty).Trim();

        // 화이트리스트 매핑
        return key switch
        {
            "Id" => "ORDER BY Id ASC",
            "IdDesc" => "ORDER BY Id DESC",

            "Name" => "ORDER BY Name ASC, Id DESC",
            "NameDesc" => "ORDER BY Name DESC, Id DESC",

            "Type" => "ORDER BY [Type] ASC, IsRequired ASC, Id DESC",
            "TypeDesc" => "ORDER BY [Type] DESC, IsRequired DESC, Id DESC",

            "IsRequired" => "ORDER BY IsRequired ASC, [Type] ASC, Id DESC",
            "IsRequiredDesc" => "ORDER BY IsRequired DESC, [Type] DESC, Id DESC",

            "Active" => "ORDER BY Active ASC, Id DESC",
            "ActiveDesc" => "ORDER BY Active DESC, Id DESC",

            "ApplicantType" => "ORDER BY ApplicantType ASC, Id DESC",
            "ApplicantTypeDesc" => "ORDER BY ApplicantType DESC, Id DESC",

            "CreatedAt" => "ORDER BY CreatedAt ASC, Id DESC",
            "CreatedAtDesc" => "ORDER BY CreatedAt DESC, Id DESC",

            _ => "ORDER BY Id DESC"
        };
    }

    #endregion

    #region Helpers

    private static DefaultAttachment Map(SqlDataReader r) => new DefaultAttachment
    {
        Id = r.GetInt64(0),
        Active = r.IsDBNull(1) ? (bool?)null : r.GetBoolean(1),
        CreatedAt = r.IsDBNull(2) ? (DateTimeOffset?)null : r.GetDateTimeOffset(2),
        CreatedBy = r.IsDBNull(3) ? null : r.GetString(3),
        Name = r.IsDBNull(4) ? null : r.GetString(4),
        ApplicantType = r.IsDBNull(5) ? (int?)null : r.GetInt32(5),
        Type = r.IsDBNull(6) ? null : r.GetString(6),
        IsRequired = r.IsDBNull(7) ? (bool?)null : r.GetBoolean(7)
    };

    private static SqlParameter SqlParam(string name, SqlDbType type, object value, int? size = null)
    {
        var p = new SqlParameter(name, type) { Value = value ?? DBNull.Value };
        if (size.HasValue)
        {
            p.Size = size.Value;
        }
        else if (type == SqlDbType.NVarChar && value is string s && s.Length > 4000)
        {
            p.Size = -1; // nvarchar(max)
        }
        return p;
    }

    private static SqlParameter Clone(SqlParameter p)
        => new SqlParameter(p.ParameterName, p.SqlDbType)
        {
            Value = p.Value,
            Size = p.Size,
            Precision = p.Precision,
            Scale = p.Scale
        };

    #endregion
}
