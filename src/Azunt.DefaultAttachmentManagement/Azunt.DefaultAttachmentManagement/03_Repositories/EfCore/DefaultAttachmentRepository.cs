using System;
using System.Linq;
using System.Threading.Tasks;
using Azunt.Models.Common; // ArticleSet 네임스페이스 확인
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Azunt.DefaultAttachmentManagement;

/// <summary>
/// EF Core 기반의 DefaultAttachment 리포지토리 구현체입니다.
/// CRUD, 검색, 페이징, 정렬을 지원하며 멀티 테넌트 연결 문자열을 처리할 수 있습니다.
/// </summary>
public class DefaultAttachmentRepository : IDefaultAttachmentRepository
{
    private readonly DefaultAttachmentDbContextFactory _factory;
    private readonly ILogger<DefaultAttachmentRepository> _logger;

    public DefaultAttachmentRepository(
        DefaultAttachmentDbContextFactory factory,
        ILoggerFactory loggerFactory)
    {
        _factory = factory;
        _logger = loggerFactory.CreateLogger<DefaultAttachmentRepository>();
    }

    protected virtual DefaultAttachmentDbContext CreateContext(string? connectionString) =>
        string.IsNullOrEmpty(connectionString)
            ? _factory.CreateDbContext()
            : _factory.CreateDbContext(connectionString);

    /// <summary>
    /// 신규 첨부파일 추가
    /// </summary>
    public async Task<DefaultAttachment> AddAsync(DefaultAttachment model, string? connectionString = null)
    {
        await using var context = CreateContext(connectionString);

        // CreatedAt은 DB 기본값(SYSDATETIMEOFFSET())을 사용하도록 값을 지정하지 않습니다.
        context.DefaultAttachments.Add(model);
        await context.SaveChangesAsync();
        return model;
    }

    /// <summary>
    /// 전체 목록 조회 (기본: Id DESC)
    /// </summary>
    public async Task<List<DefaultAttachment>> GetAllAsync(string? connectionString = null)
    {
        await using var context = CreateContext(connectionString);
        return await context.DefaultAttachments
            .OrderByDescending(m => m.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Id로 단건 조회 (없으면 빈 개체 반환)
    /// </summary>
    public async Task<DefaultAttachment> GetByIdAsync(long id, string? connectionString = null)
    {
        await using var context = CreateContext(connectionString);
        return await context.DefaultAttachments.SingleOrDefaultAsync(m => m.Id == id)
               ?? new DefaultAttachment();
    }

    /// <summary>
    /// 첨부파일 수정 (필요 필드만 업데이트)
    /// </summary>
    public async Task<bool> UpdateAsync(DefaultAttachment model, string? connectionString = null)
    {
        await using var context = CreateContext(connectionString);

        // 전역 NoTracking이어도 이 쿼리는 트래킹 모드로 강제
        var entity = await context.DefaultAttachments
            .AsTracking()
            .FirstOrDefaultAsync(e => e.Id == model.Id);

        if (entity is null) return false;

        entity.Active = model.Active;
        entity.Name = model.Name;
        entity.Type = model.Type;
        entity.IsRequired = model.IsRequired;
        entity.CreatedBy = model.CreatedBy;
        entity.ApplicantType = model.ApplicantType;

        return await context.SaveChangesAsync() > 0;
    }

    /// <summary>
    /// 첨부파일 삭제
    /// </summary>
    public async Task<bool> DeleteAsync(long id, string? connectionString = null)
    {
        await using var context = CreateContext(connectionString);
        var entity = await context.DefaultAttachments.FindAsync(id);
        if (entity == null) return false;

        context.DefaultAttachments.Remove(entity);
        return await context.SaveChangesAsync() > 0;
    }

    /// <summary>
    /// 페이징 + 검색 + 정렬 지원 목록 조회
    /// </summary>
    public async Task<ArticleSet<DefaultAttachment, int>> GetAllAsync<TParentIdentifier>(
        int pageIndex,
        int pageSize,
        string searchField,    // 현재는 미사용(호환용). 필요 시 필드별 검색으로 확장 가능.
        string searchQuery,
        string sortOrder,
        TParentIdentifier parentIdentifier,
        string? connectionString = null)
    {
        await using var context = CreateContext(connectionString);

        var query = context.DefaultAttachments.AsQueryable();

        // 검색: Name / Type / CreatedBy LIKE, IsRequired/Active는 true/false 파싱 시 필터
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var like = $"%{searchQuery}%";
            query = query.Where(m =>
                (m.Name != null && EF.Functions.Like(m.Name, like)) ||
                (m.Type != null && EF.Functions.Like(m.Type, like)) ||
                (m.CreatedBy != null && EF.Functions.Like(m.CreatedBy, like))
            );

            if (bool.TryParse(searchQuery, out var boolQuery))
            {
                query = query.Where(m =>
                    (m.IsRequired != null && m.IsRequired == boolQuery) ||
                    (m.Active != null && m.Active == boolQuery));
            }

            if (int.TryParse(searchQuery, out var intQuery))
            {
                // ApplicantType 숫자 검색
                query = query.Where(m => (m.ApplicantType ?? 0) == intQuery);
            }
        }

        // 정렬 적용
        query = ApplySorting(query, sortOrder);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip(pageIndex * pageSize) // pageIndex가 0 기반이라는 전제
            .Take(pageSize)
            .ToListAsync();

        return new ArticleSet<DefaultAttachment, int>(items, totalCount);
    }

    /// <summary>
    /// sortOrder 문자열을 해석하여 안전한 정렬을 적용합니다.
    /// 지원: Id, IdDesc, Name, NameDesc, Type, TypeDesc, IsRequired, IsRequiredDesc, CreatedAt, CreatedAtDesc, Active, ActiveDesc, ApplicantType, ApplicantTypeDesc
    /// 기본: IdDesc
    /// </summary>
    private static IQueryable<DefaultAttachment> ApplySorting(IQueryable<DefaultAttachment> query, string sortOrder)
    {
        var key = (sortOrder ?? string.Empty).Trim();

        return key switch
        {
            "Id" => query.OrderBy(e => e.Id),
            "IdDesc" => query.OrderByDescending(e => e.Id),

            "Name" => query.OrderBy(e => e.Name ?? string.Empty)
                                       .ThenByDescending(e => e.Id),
            "NameDesc" => query.OrderByDescending(e => e.Name ?? string.Empty)
                                       .ThenByDescending(e => e.Id),

            "Type" => query.OrderBy(e => e.Type ?? string.Empty)
                                       .ThenBy(e => e.IsRequired ?? false)
                                       .ThenByDescending(e => e.Id),
            "TypeDesc" => query.OrderByDescending(e => e.Type ?? string.Empty)
                                       .ThenByDescending(e => e.IsRequired ?? false)
                                       .ThenByDescending(e => e.Id),

            "IsRequired" => query.OrderBy(e => e.IsRequired ?? false)
                                       .ThenBy(e => e.Type ?? string.Empty)
                                       .ThenByDescending(e => e.Id),
            "IsRequiredDesc" => query.OrderByDescending(e => e.IsRequired ?? false)
                                       .ThenByDescending(e => e.Type ?? string.Empty)
                                       .ThenByDescending(e => e.Id),

            "Active" => query.OrderBy(e => e.Active ?? false)
                                       .ThenByDescending(e => e.Id),
            "ActiveDesc" => query.OrderByDescending(e => e.Active ?? false)
                                       .ThenByDescending(e => e.Id),

            "ApplicantType" => query.OrderBy(e => e.ApplicantType ?? 0)
                                       .ThenByDescending(e => e.Id),
            "ApplicantTypeDesc" => query.OrderByDescending(e => e.ApplicantType ?? 0)
                                       .ThenByDescending(e => e.Id),

            "CreatedAt" => query.OrderBy(e => e.CreatedAt)
                                       .ThenByDescending(e => e.Id),
            "CreatedAtDesc" => query.OrderByDescending(e => e.CreatedAt)
                                       .ThenByDescending(e => e.Id),

            _ => query.OrderByDescending(e => e.Id) // 기본값
        };
    }
}
