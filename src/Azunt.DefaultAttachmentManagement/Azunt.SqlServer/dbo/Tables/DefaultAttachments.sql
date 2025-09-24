-- [0][0] 첨부파일 테이블: DefaultAttachments 
CREATE TABLE [dbo].[DefaultAttachments]
(
    [Id]           BIGINT IDENTITY (1, 1) NOT NULL PRIMARY KEY,         -- 고유 ID (자동 증가)
    [Active]       BIT              NULL DEFAULT (1),                   -- 활성 여부 (기본값: 1=활성)
    [CreatedAt]    DATETIMEOFFSET   NULL DEFAULT SYSDATETIMEOFFSET(),   -- 생성 시각
    [CreatedBy]    NVARCHAR(255)    NULL,                               -- 생성자
    [Name]         NVARCHAR(MAX)    NULL,                               -- 첨부파일 이름
    [ApplicantType] INT             NULL DEFAULT (0),                   -- 신청자 유형 (예: 0=기본, 1=Vendor, 2=Employee 등)
    [Type]         NVARCHAR(255)    NULL,                               -- 첨부파일 유형
    [IsRequired]   BIT              NULL DEFAULT (1)                    -- 필수 여부 (1=필수, 0=선택, NULL=미정의)
);