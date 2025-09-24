using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azunt.Models.Common;        // ArticleSet<,>
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Azunt.DefaultAttachmentManagement;

/// <summary>
/// Dapper 기반 DefaultAttachment 리포지토리 구현체
/// - CRUD
/// - SQL 레벨 검색/정렬/페이징 (OFFSET/FETCH)
/// - 멀티 테넌트 연결 문자열 지원
/// - DB DEFAULT(SYSDATETIMEOFFSET, BIT/INT DEFAULT) 보존을 위해 null일 때 컬럼 생략 삽입
/// </summary>
public class DefaultAttachmentRepositoryDapper : IDefaultAttachmentRepository
{
    private readonly string _defaultConnectionString;
    private readonly ILogger<DefaultAttachmentRepositoryDapper> _logger;

    public DefaultAttachmentRepositoryDapper(string defaultConnectionString, ILoggerFactory loggerFactory)
    {
        _defaultConnectionString = defaultConnectionString ?? throw new ArgumentNullException(nameof(defaultConnectionString));
        _logger = loggerFactory.CreateLogger<DefaultAttachmentRepositoryDapper>();
    }

    private SqlConnection GetConnection(string? connectionString)
        => new SqlConnection(connectionString ?? _defaultConnectionString);

    #region CRUD

    public async Task<DefaultAttachment> AddAsync(DefaultAttachment model, string? connectionString = null)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));

        await using var conn = GetConnection(connectionString);
        await conn.OpenAsync();

        // INSERT 문 동적 구성: null인 기본값 컬럼은 생략하여 DB DEFAULT 사용
        var cols = new List<string> { "Name", "CreatedBy", "[Type]" };
        var vals = new List<string> { "@Name", "@CreatedBy", "@Type" };

        var dp = new DynamicParameters();

        // nvarchar(max): size = -1
        dp.Add("@Name", (object?)model.Name ?? DBNull.Value, DbType.String, ParameterDirection.Input, size: -1);
        dp.Add("@CreatedBy", (object?)model.CreatedBy ?? DBNull.Value, DbType.String, ParameterDirection.Input, size: 255);
        dp.Add("@Type", (object?)model.Type ?? DBNull.Value, DbType.String, ParameterDirection.Input, size: 255);

        if (model.Active.HasValue)
        {
            cols.Add("Active"); vals.Add("@Active");
            dp.Add("@Active", model.Active.Value, DbType.Boolean);
        }
        if (model.IsRequired.HasValue)
        {
            cols.Add("IsRequired"); vals.Add("@IsRequired");
            dp.Add("@IsRequired", model.IsRequired.Value, DbType.Boolean);
        }
        if (model.ApplicantType.HasValue)
        {
            cols.Add("ApplicantType"); vals.Add("@ApplicantType");
            dp.Add("@ApplicantType", model.ApplicantType.Value, DbType.Int32);
        }
        // CreatedAt은 DB 기본값(SYSDATETIMEOFFSET()) 사용 → 컬럼 생략

        var sql = new StringBuilder()
            .Append("INSERT INTO dbo.DefaultAttachments (")
            .Append(string.Join(", ", cols))
            .Append(") OUTPUT INSERTED.Id VALUES (")
            .Append(string.Join(", ", vals))
            .Append(");")
            .ToString();

        model.Id = await conn.ExecuteScalarAsync<long>(sql, dp);
        return model;
    }

    public async Task<List<DefaultAttachment>> GetAllAsync(string? connectionString = null)
    {
        await using var conn = GetConnection(connectionString);
        const string sql = @"
SELECT Id, Active, CreatedAt, CreatedBy, Name, ApplicantType, [Type], IsRequired
FROM dbo.DefaultAttachments
ORDER BY Id DESC;";
        var rows = await conn.QueryAsync<DefaultAttachment>(sql);
        return rows.ToList();
    }

    public async Task<DefaultAttachment> GetByIdAsync(long id, string? connectionString = null)
    {
        await using var conn = GetConnection(connectionString);
        const string sql = @"
SELECT Id, Active, CreatedAt, CreatedBy, Name, ApplicantType, [Type], IsRequired
FROM dbo.DefaultAttachments
WHERE Id = @Id;";
        var model = await conn.QuerySingleOrDefaultAsync<DefaultAttachment>(sql, new { Id = id });
        return model ?? new DefaultAttachment(); // 컨벤션 유지
    }

    public async Task<bool> UpdateAsync(DefaultAttachment model, string? connectionString = null)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));

        await using var conn = GetConnection(connectionString);
        const string sql = @"
UPDATE dbo.DefaultAttachments
   SET Active        = @Active,
       Name          = @Name,
       [Type]        = @Type,
       IsRequired    = @IsRequired,
       CreatedBy     = @CreatedBy,
       ApplicantType = @ApplicantType
 WHERE Id            = @Id;";

        var dp = new DynamicParameters();
        dp.Add("@Active", (object?)model.Active ?? DBNull.Value, DbType.Boolean);
        dp.Add("@Name", (object?)model.Name ?? DBNull.Value, DbType.String, size: -1);
        dp.Add("@Type", (object?)model.Type ?? DBNull.Value, DbType.String, size: 255);
        dp.Add("@IsRequired", (object?)model.IsRequired ?? DBNull.Value, DbType.Boolean);
        dp.Add("@CreatedBy", (object?)model.CreatedBy ?? DBNull.Value, DbType.String, size: 255);
        dp.Add("@ApplicantType", (object?)model.ApplicantType ?? DBNull.Value, DbType.Int32);
        dp.Add("@Id", model.Id, DbType.Int64);

        var rows = await conn.ExecuteAsync(sql, dp);
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id, string? connectionString = null)
    {
        await using var conn = GetConnection(connectionString);
        const string sql = "DELETE FROM dbo.DefaultAttachments WHERE Id = @Id;";
        var rows = await conn.ExecuteAsync(sql, new { Id = id });
        return rows > 0;
    }

    #endregion

    #region Paged/Search/Sort

    public async Task<ArticleSet<DefaultAttachment, int>> GetAllAsync<TParentIdentifier>(
        int pageIndex,
        int pageSize,
        string searchField,   // 현재 미사용(호환용). 필요 시 필드별 검색으로 확장.
        string searchQuery,
        string sortOrder,
        TParentIdentifier parentIdentifier,
        string? connectionString = null)
    {
        if (pageIndex < 0) pageIndex = 0;
        if (pageSize <= 0) pageSize = 10;

        await using var conn = GetConnection(connectionString);

        // WHERE
        var where = new StringBuilder();
        var dp = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var like = $"%{searchQuery}%";
            where.AppendLine("WHERE (")
                 .AppendLine("       (Name      IS NOT NULL AND Name      LIKE @qName)")
                 .AppendLine("    OR (CreatedBy IS NOT NULL AND CreatedBy LIKE @qCreatedBy)")
                 .AppendLine("    OR ([Type]    IS NOT NULL AND [Type]    LIKE @qType)");

            dp.Add("@qName", like, DbType.String, size: -1);
            dp.Add("@qCreatedBy", like, DbType.String, size: 255);
            dp.Add("@qType", like, DbType.String, size: 255);

            if (bool.TryParse(searchQuery, out var boolQuery))
            {
                where.AppendLine("    OR (IsRequired = @qBool) OR (Active = @qBool)");
                dp.Add("@qBool", boolQuery, DbType.Boolean);
            }
            if (int.TryParse(searchQuery, out var intQuery))
            {
                where.AppendLine("    OR (ApplicantType = @qInt)");
                dp.Add("@qInt", intQuery, DbType.Int32);
            }
            where.AppendLine(")");
        }

        var orderBy = BuildOrderBy(sortOrder);

        // COUNT
        var countSql = $@"
SELECT COUNT(1)
FROM dbo.DefaultAttachments
{where};";

        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, dp);

        // PAGE
        var offset = pageIndex * pageSize;
        var pageSql = $@"
SELECT Id, Active, CreatedAt, CreatedBy, Name, ApplicantType, [Type], IsRequired
FROM dbo.DefaultAttachments
{where}
{orderBy}
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;";

        dp.Add("@offset", offset, DbType.Int32);
        dp.Add("@pageSize", pageSize, DbType.Int32);

        var items = (await conn.QueryAsync<DefaultAttachment>(pageSql, dp)).ToList();

        return new ArticleSet<DefaultAttachment, int>(items, totalCount);
    }

    private static string BuildOrderBy(string sortOrder)
    {
        var key = (sortOrder ?? string.Empty).Trim();

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
}
