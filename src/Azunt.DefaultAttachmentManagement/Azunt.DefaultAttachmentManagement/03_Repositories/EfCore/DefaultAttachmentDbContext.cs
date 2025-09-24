using Microsoft.EntityFrameworkCore;

namespace Azunt.DefaultAttachmentManagement
{
    /// <summary>
    /// DefaultAttachmentApp에서 사용하는 EF Core DbContext.
    /// 애그리게이트 루트(DefaultAttachment 등)에 대한 매핑과 공통 규칙을 구성합니다.
    /// </summary>
    public class DefaultAttachmentDbContext : DbContext
    {
        /// <summary>
        /// DbContextOptions를 받는 기본 생성자.
        /// 주로 Program.cs/Startup.cs 등록에서 사용합니다.
        /// </summary>
        public DefaultAttachmentDbContext(DbContextOptions<DefaultAttachmentDbContext> options)
            : base(options)
        {
            // 기본 조회는 변경 추적 없이 수행 (쓰기 시나리오에서는 AsTracking() 사용)
            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }

        /// <summary>
        /// DefaultAttachments 테이블에 매핑되는 엔터티 집합.
        /// </summary>
        public DbSet<DefaultAttachment> DefaultAttachments { get; set; } = null!;

        /// <summary>
        /// 모델 구성: 컬럼 타입/기본값 등 스키마 규칙 정의.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var e = modelBuilder.Entity<DefaultAttachment>();

            // 매핑 테이블명 명시
            e.ToTable("DefaultAttachments");

            // PK (Id) 값은 DB에서 생성됨
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();

            // CreatedAt: datetimeoffset + 기본값 SYSDATETIMEOFFSET(), 저장소 생성 값으로 처리
            e.Property(p => p.CreatedAt)
                .HasColumnType("datetimeoffset")
                .HasDefaultValueSql("SYSDATETIMEOFFSET()")
                .ValueGeneratedOnAdd();

            // Active: BIT NULL DEFAULT(1)
            e.Property(p => p.Active)
                .HasColumnType("bit")
                .HasDefaultValue(true);

            // IsRequired: BIT NULL DEFAULT(1)
            e.Property(p => p.IsRequired)
                .HasColumnType("bit")
                .HasDefaultValue(true);

            // ApplicantType: INT NULL DEFAULT(0)
            e.Property(p => p.ApplicantType)
                .HasColumnType("int")
                .HasDefaultValue(0);

            // CreatedBy: NVARCHAR(255)
            e.Property(p => p.CreatedBy)
                .HasMaxLength(255);

            // Type: NVARCHAR(255)
            e.Property(p => p.Type)
                .HasMaxLength(255);

            // Name: NVARCHAR(MAX)
            e.Property(p => p.Name)
                .HasColumnType("nvarchar(max)");

            // 조회 자주 쓰는 컬럼 인덱스(선택)
            e.HasIndex(p => p.Active);
            e.HasIndex(p => p.ApplicantType);
        }
    }
}
