using Azunt.Models.Common;

namespace Azunt.DefaultAttachmentManagement;

public interface IDefaultAttachmentRepository
{
    Task<DefaultAttachment> AddAsync(DefaultAttachment model, string? connectionString = null);
    Task<List<DefaultAttachment>> GetAllAsync(string? connectionString = null);
    Task<DefaultAttachment> GetByIdAsync(long id, string? connectionString = null);
    Task<bool> UpdateAsync(DefaultAttachment model, string? connectionString = null);
    Task<bool> DeleteAsync(long id, string? connectionString = null);
    Task<ArticleSet<DefaultAttachment, int>> GetAllAsync<TParentIdentifier>(int pageIndex, int pageSize, string searchField, string searchQuery, string sortOrder, TParentIdentifier parentIdentifier, string? connectionString = null);
}