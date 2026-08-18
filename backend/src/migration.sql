IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [ActivityLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [Timestamp] datetimeoffset NOT NULL,
        [Level] int NOT NULL,
        [Event] nvarchar(64) NOT NULL,
        [Message] nvarchar(2000) NOT NULL,
        [SearchJobId] uniqueidentifier NULL,
        [UserId] uniqueidentifier NULL,
        [ElapsedMs] bigint NULL,
        [ContextJson] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_ActivityLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [AppSettings] (
        [Id] uniqueidentifier NOT NULL,
        [Key] nvarchar(128) NOT NULL,
        [Value] nvarchar(2000) NOT NULL,
        [Description] nvarchar(512) NOT NULL,
        [IsSecret] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_AppSettings] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditEntries] (
        [Id] bigint NOT NULL IDENTITY,
        [Timestamp] datetimeoffset NOT NULL,
        [UserId] uniqueidentifier NULL,
        [UserName] nvarchar(256) NOT NULL,
        [Action] nvarchar(128) NOT NULL,
        [EntityType] nvarchar(128) NOT NULL,
        [EntityId] nvarchar(128) NULL,
        [IpAddress] nvarchar(64) NULL,
        [UserAgent] nvarchar(512) NULL,
        [Succeeded] bit NOT NULL,
        [DetailsJson] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_AuditEntries] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [Exports] (
        [Id] uniqueidentifier NOT NULL,
        [FileName] nvarchar(256) NOT NULL,
        [Format] nvarchar(16) NOT NULL,
        [Template] nvarchar(32) NOT NULL,
        [RowCount] int NOT NULL,
        [ByteSize] bigint NOT NULL,
        [FiltersJson] nvarchar(max) NOT NULL,
        [StorageKey] nvarchar(512) NULL,
        [RequestedByUserId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Exports] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [Permissions] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [Group] nvarchar(64) NOT NULL,
        [Description] nvarchar(512) NOT NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] uniqueidentifier NOT NULL,
        [Description] nvarchar(512) NOT NULL,
        [IsSystemRole] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [SearchJobs] (
        [Id] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        [RequestJson] nvarchar(max) NOT NULL,
        [TotalTasks] int NOT NULL,
        [CompletedTasks] int NOT NULL,
        [Found] int NOT NULL,
        [Saved] int NOT NULL,
        [Duplicates] int NOT NULL,
        [Enriched] int NOT NULL,
        [EnrichmentFailed] int NOT NULL,
        [EmailsVerified] int NOT NULL,
        [WhatsAppReachable] int NOT NULL,
        [Skipped] int NOT NULL,
        [Failed] int NOT NULL,
        [CurrentTask] nvarchar(256) NOT NULL,
        [StartedAt] datetimeoffset NOT NULL,
        [FinishedAt] datetimeoffset NULL,
        [HeartbeatAt] datetimeoffset NULL,
        [StopRequested] bit NOT NULL,
        [Error] nvarchar(2000) NULL,
        [RequestedByUserId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_SearchJobs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [LastLoginAt] datetimeoffset NULL,
        [SecurityVersion] int NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [RoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] uniqueidentifier NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_RoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RoleClaims_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [RoleId] uniqueidentifier NOT NULL,
        [PermissionId] uniqueidentifier NOT NULL,
        [GrantedAt] datetimeoffset NOT NULL,
        [GrantedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RoleId], [PermissionId]),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [Businesses] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(256) NOT NULL,
        [Category] nvarchar(128) NOT NULL,
        [Country] nvarchar(128) NOT NULL,
        [State] nvarchar(128) NOT NULL,
        [City] nvarchar(128) NOT NULL,
        [Address] nvarchar(512) NOT NULL,
        [Phone] nvarchar(64) NOT NULL,
        [Website] nvarchar(512) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [EmailStatus] int NOT NULL,
        [WhatsApp] nvarchar(512) NOT NULL,
        [WhatsAppStatus] int NOT NULL,
        [Facebook] nvarchar(512) NOT NULL,
        [Instagram] nvarchar(512) NOT NULL,
        [LinkedIn] nvarchar(512) NOT NULL,
        [Latitude] float NULL,
        [Longitude] float NULL,
        [Rating] float(3) NULL,
        [ReviewCount] int NULL,
        [MapsUrl] nvarchar(1024) NOT NULL,
        [Source] int NOT NULL,
        [Status] int NOT NULL,
        [Notes] nvarchar(2000) NOT NULL,
        [SearchJobId] uniqueidentifier NULL,
        [DedupeWebsiteKey] nvarchar(256) NULL,
        [DedupePhoneKey] nvarchar(32) NULL,
        [DedupeNameKey] nvarchar(384) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Businesses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Businesses_SearchJobs_SearchJobId] FOREIGN KEY ([SearchJobId]) REFERENCES [SearchJobs] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [TokenHash] nvarchar(128) NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedByIp] nvarchar(64) NULL,
        [RevokedAt] datetimeoffset NULL,
        [RevokedByIp] nvarchar(64) NULL,
        [RevokedReason] nvarchar(256) NULL,
        [ReplacedByTokenHash] nvarchar(128) NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [UserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_UserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserClaims_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [UserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_UserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_UserLogins_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [UserPermissions] (
        [UserId] uniqueidentifier NOT NULL,
        [PermissionId] uniqueidentifier NOT NULL,
        [IsGranted] bit NOT NULL,
        [GrantedAt] datetimeoffset NOT NULL,
        [GrantedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_UserPermissions] PRIMARY KEY ([UserId], [PermissionId]),
        CONSTRAINT [FK_UserPermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserPermissions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [UserRoles] (
        [UserId] uniqueidentifier NOT NULL,
        [RoleId] uniqueidentifier NOT NULL,
        [AssignedAt] datetimeoffset NOT NULL,
        [AssignedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_UserRoles_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE TABLE [UserTokens] (
        [UserId] uniqueidentifier NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_UserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_UserTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ActivityLogs_Event] ON [ActivityLogs] ([Event]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ActivityLogs_Level] ON [ActivityLogs] ([Level]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ActivityLogs_SearchJobId] ON [ActivityLogs] ([SearchJobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ActivityLogs_Timestamp] ON [ActivityLogs] ([Timestamp]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppSettings_Key] ON [AppSettings] ([Key]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_Action] ON [AuditEntries] ([Action]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_Timestamp] ON [AuditEntries] ([Timestamp]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_UserId] ON [AuditEntries] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Businesses_Category] ON [Businesses] ([Category]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Businesses_Country_City] ON [Businesses] ([Country], [City]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Businesses_CreatedAt] ON [Businesses] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Businesses_DedupeNameKey] ON [Businesses] ([DedupeNameKey]) WHERE [DedupeNameKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Businesses_DedupePhoneKey] ON [Businesses] ([DedupePhoneKey]) WHERE [DedupePhoneKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Businesses_DedupeWebsiteKey] ON [Businesses] ([DedupeWebsiteKey]) WHERE [DedupeWebsiteKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Businesses_EmailStatus] ON [Businesses] ([EmailStatus]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Businesses_Name] ON [Businesses] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Businesses_SearchJobId] ON [Businesses] ([SearchJobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Businesses_Status] ON [Businesses] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Businesses_WhatsAppStatus] ON [Businesses] ([WhatsAppStatus]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Exports_CreatedAt] ON [Exports] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Permissions_Group] ON [Permissions] ([Group]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_Name] ON [Permissions] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId_ExpiresAt] ON [RefreshTokens] ([UserId], [ExpiresAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RoleClaims_RoleId] ON [RoleClaims] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [Roles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SearchJobs_StartedAt] ON [SearchJobs] ([StartedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SearchJobs_Status] ON [SearchJobs] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SearchJobs_Status_HeartbeatAt] ON [SearchJobs] ([Status], [HeartbeatAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserClaims_UserId] ON [UserClaims] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserLogins_UserId] ON [UserLogins] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserPermissions_PermissionId] ON [UserPermissions] ([PermissionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserRoles_RoleId] ON [UserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_IsActive] ON [Users] ([IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [Users] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801051722_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260801051722_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    ALTER TABLE [Businesses] ADD [LastContactedAt] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    ALTER TABLE [Businesses] ADD [OwnerUserId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    ALTER TABLE [Businesses] ADD [TimesContacted] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE TABLE [EmailSignatures] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [BodyHtml] nvarchar(max) NOT NULL,
        [BodyText] nvarchar(max) NOT NULL,
        [OwnerUserId] uniqueidentifier NULL,
        [IsDefault] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_EmailSignatures] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE TABLE [EmailTemplates] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [Description] nvarchar(512) NOT NULL,
        [Subject] nvarchar(256) NOT NULL,
        [BodyHtml] nvarchar(max) NOT NULL,
        [BodyText] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [SignatureId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_EmailTemplates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmailTemplates_EmailSignatures_SignatureId] FOREIGN KEY ([SignatureId]) REFERENCES [EmailSignatures] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE TABLE [EmailLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [SenderUserId] uniqueidentifier NULL,
        [SenderEmail] nvarchar(256) NOT NULL,
        [SenderName] nvarchar(256) NOT NULL,
        [ToEmail] nvarchar(256) NOT NULL,
        [ToName] nvarchar(256) NOT NULL,
        [Cc] nvarchar(512) NULL,
        [Bcc] nvarchar(512) NULL,
        [Subject] nvarchar(512) NOT NULL,
        [BodyHtml] nvarchar(max) NOT NULL,
        [TemplateId] uniqueidentifier NULL,
        [TemplateName] nvarchar(128) NOT NULL,
        [BusinessId] uniqueidentifier NULL,
        [Status] int NOT NULL,
        [Error] nvarchar(2000) NULL,
        [MessageId] nvarchar(256) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [SentAt] datetimeoffset NULL,
        [DurationMs] bigint NULL,
        CONSTRAINT [PK_EmailLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmailLogs_EmailTemplates_TemplateId] FOREIGN KEY ([TemplateId]) REFERENCES [EmailTemplates] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE INDEX [IX_Businesses_OwnerUserId] ON [Businesses] ([OwnerUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE INDEX [IX_Businesses_OwnerUserId_CreatedAt] ON [Businesses] ([OwnerUserId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE INDEX [IX_EmailLogs_BusinessId] ON [EmailLogs] ([BusinessId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE INDEX [IX_EmailLogs_CreatedAt] ON [EmailLogs] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE INDEX [IX_EmailLogs_SenderUserId_CreatedAt] ON [EmailLogs] ([SenderUserId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE INDEX [IX_EmailLogs_Status] ON [EmailLogs] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE INDEX [IX_EmailLogs_TemplateId] ON [EmailLogs] ([TemplateId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE INDEX [IX_EmailLogs_ToEmail] ON [EmailLogs] ([ToEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE INDEX [IX_EmailSignatures_IsDefault] ON [EmailSignatures] ([IsDefault]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE INDEX [IX_EmailSignatures_OwnerUserId] ON [EmailSignatures] ([OwnerUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE INDEX [IX_EmailTemplates_IsActive] ON [EmailTemplates] ([IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EmailTemplates_Name] ON [EmailTemplates] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    CREATE INDEX [IX_EmailTemplates_SignatureId] ON [EmailTemplates] ([SignatureId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803074506_LeadOwnershipAndEmail'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260803074506_LeadOwnershipAndEmail', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803091747_DurableJobQueue'
)
BEGIN
    ALTER TABLE [SearchJobs] ADD [Attempts] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803091747_DurableJobQueue'
)
BEGIN
    ALTER TABLE [SearchJobs] ADD [CompletedTaskKeysJson] nvarchar(max) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803091747_DurableJobQueue'
)
BEGIN
    ALTER TABLE [SearchJobs] ADD [LeaseExpiresAt] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803091747_DurableJobQueue'
)
BEGIN
    ALTER TABLE [SearchJobs] ADD [LeaseOwner] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803091747_DurableJobQueue'
)
BEGIN
    ALTER TABLE [SearchJobs] ADD [RecentResultsJson] nvarchar(max) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803091747_DurableJobQueue'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260803091747_DurableJobQueue', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803134151_RatingFullPrecision'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Businesses]') AND [c].[name] = N'Rating');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Businesses] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [Businesses] ALTER COLUMN [Rating] float NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803134151_RatingFullPrecision'
)
BEGIN
    UPDATE [Businesses] SET [Rating] = ROUND([Rating], 1) WHERE [Rating] IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803134151_RatingFullPrecision'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260803134151_RatingFullPrecision', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804051402_AddWhatsAppFeature'
)
BEGIN
    ALTER TABLE [Businesses] ADD [LastWhatsAppContactedAt] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804051402_AddWhatsAppFeature'
)
BEGIN
    ALTER TABLE [Businesses] ADD [WhatsAppTimesContacted] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804051402_AddWhatsAppFeature'
)
BEGIN
    CREATE TABLE [WhatsAppTemplates] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [Description] nvarchar(512) NOT NULL,
        [Message] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_WhatsAppTemplates] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804051402_AddWhatsAppFeature'
)
BEGIN
    CREATE TABLE [WhatsAppContactLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [SenderUserId] uniqueidentifier NULL,
        [SenderEmail] nvarchar(256) NOT NULL,
        [BusinessId] uniqueidentifier NOT NULL,
        [ToName] nvarchar(256) NOT NULL,
        [ToPhone] nvarchar(64) NOT NULL,
        [TemplateId] uniqueidentifier NULL,
        [TemplateName] nvarchar(128) NOT NULL,
        [Message] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_WhatsAppContactLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WhatsAppContactLogs_WhatsAppTemplates_TemplateId] FOREIGN KEY ([TemplateId]) REFERENCES [WhatsAppTemplates] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804051402_AddWhatsAppFeature'
)
BEGIN
    CREATE INDEX [IX_WhatsAppContactLogs_BusinessId] ON [WhatsAppContactLogs] ([BusinessId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804051402_AddWhatsAppFeature'
)
BEGIN
    CREATE INDEX [IX_WhatsAppContactLogs_CreatedAt] ON [WhatsAppContactLogs] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804051402_AddWhatsAppFeature'
)
BEGIN
    CREATE INDEX [IX_WhatsAppContactLogs_SenderUserId_CreatedAt] ON [WhatsAppContactLogs] ([SenderUserId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804051402_AddWhatsAppFeature'
)
BEGIN
    CREATE INDEX [IX_WhatsAppContactLogs_TemplateId] ON [WhatsAppContactLogs] ([TemplateId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804051402_AddWhatsAppFeature'
)
BEGIN
    CREATE INDEX [IX_WhatsAppTemplates_IsActive] ON [WhatsAppTemplates] ([IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804051402_AddWhatsAppFeature'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WhatsAppTemplates_Name] ON [WhatsAppTemplates] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804051402_AddWhatsAppFeature'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804051402_AddWhatsAppFeature', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    ALTER TABLE [Businesses] ADD [CompanyDescription] nvarchar(4000) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    ALTER TABLE [Businesses] ADD [EmployeeCount] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    ALTER TABLE [Businesses] ADD [ImageUrlsJson] nvarchar(max) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    ALTER TABLE [Businesses] ADD [Industry] nvarchar(256) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    ALTER TABLE [Businesses] ADD [LastLinkedInEnrichedAt] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    ALTER TABLE [Businesses] ADD [LastMapsEnrichedAt] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    ALTER TABLE [Businesses] ADD [OpeningHoursJson] nvarchar(max) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    ALTER TABLE [Businesses] ADD [PermanentlyClosed] bit NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    ALTER TABLE [Businesses] ADD [PlaceId] nvarchar(256) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    ALTER TABLE [Businesses] ADD [PostalCode] nvarchar(32) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    CREATE TABLE [People] (
        [Id] uniqueidentifier NOT NULL,
        [BusinessId] uniqueidentifier NULL,
        [CompanyName] nvarchar(256) NOT NULL,
        [FullName] nvarchar(256) NOT NULL,
        [JobTitle] nvarchar(256) NOT NULL,
        [Headline] nvarchar(512) NOT NULL,
        [LinkedInUrl] nvarchar(512) NOT NULL,
        [Location] nvarchar(256) NOT NULL,
        [ExperienceJson] nvarchar(max) NOT NULL,
        [EducationJson] nvarchar(max) NOT NULL,
        [SkillsJson] nvarchar(max) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [Phone] nvarchar(64) NOT NULL,
        [IsDecisionMaker] bit NOT NULL,
        [DecisionMakerRole] nvarchar(256) NOT NULL,
        [OwnerUserId] uniqueidentifier NULL,
        [SearchJobId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_People] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_People_Businesses_BusinessId] FOREIGN KEY ([BusinessId]) REFERENCES [Businesses] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    CREATE INDEX [IX_People_BusinessId] ON [People] ([BusinessId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    CREATE INDEX [IX_People_IsDecisionMaker] ON [People] ([IsDecisionMaker]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    CREATE INDEX [IX_People_LinkedInUrl] ON [People] ([LinkedInUrl]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    CREATE INDEX [IX_People_OwnerUserId] ON [People] ([OwnerUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    CREATE INDEX [IX_People_OwnerUserId_CreatedAt] ON [People] ([OwnerUserId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804155315_AddLinkedInAndMapsEnrichment'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804155315_AddLinkedInAndMapsEnrichment', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804165353_AddPeopleSearchGuard'
)
BEGIN
    ALTER TABLE [Businesses] ADD [LastPeopleSearchedAt] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804165353_AddPeopleSearchGuard'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804165353_AddPeopleSearchGuard', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805102953_AddJobKind'
)
BEGIN
    ALTER TABLE [SearchJobs] ADD [Kind] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805102953_AddJobKind'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805102953_AddJobKind', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813112855_AddLinkedInAccountSessions'
)
BEGIN
    CREATE TABLE [LinkedInAccountSessions] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [StorageStateJson] nvarchar(max) NOT NULL,
        [UploadedAt] datetimeoffset NOT NULL,
        [BudgetDate] date NOT NULL,
        [SearchesToday] int NOT NULL,
        [RestrictedAt] datetimeoffset NULL,
        [RestrictedReason] nvarchar(512) NULL,
        CONSTRAINT [PK_LinkedInAccountSessions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813112855_AddLinkedInAccountSessions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LinkedInAccountSessions_UserId] ON [LinkedInAccountSessions] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813112855_AddLinkedInAccountSessions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260813112855_AddLinkedInAccountSessions', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818104226_AddEmailValidationPipeline'
)
BEGIN
    ALTER TABLE [Businesses] ADD [EmailConfidence] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818104226_AddEmailValidationPipeline'
)
BEGIN
    ALTER TABLE [Businesses] ADD [EmailIsCatchAll] bit NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818104226_AddEmailValidationPipeline'
)
BEGIN
    ALTER TABLE [Businesses] ADD [EmailIsDisposable] bit NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818104226_AddEmailValidationPipeline'
)
BEGIN
    ALTER TABLE [Businesses] ADD [EmailIsRoleAccount] bit NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818104226_AddEmailValidationPipeline'
)
BEGIN
    ALTER TABLE [Businesses] ADD [EmailValidatedAt] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818104226_AddEmailValidationPipeline'
)
BEGIN
    ALTER TABLE [Businesses] ADD [EmailValidationDetailsJson] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818104226_AddEmailValidationPipeline'
)
BEGIN
    CREATE TABLE [EmailBounceChecks] (
        [Id] uniqueidentifier NOT NULL,
        [BusinessId] uniqueidentifier NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [Status] int NOT NULL,
        [SentAt] datetimeoffset NOT NULL,
        [MessageId] nvarchar(256) NOT NULL,
        [ResolvedAt] datetimeoffset NULL,
        [BounceReason] nvarchar(512) NULL,
        CONSTRAINT [PK_EmailBounceChecks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmailBounceChecks_Businesses_BusinessId] FOREIGN KEY ([BusinessId]) REFERENCES [Businesses] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818104226_AddEmailValidationPipeline'
)
BEGIN
    CREATE INDEX [IX_Businesses_EmailValidatedAt] ON [Businesses] ([EmailValidatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818104226_AddEmailValidationPipeline'
)
BEGIN
    CREATE INDEX [IX_EmailBounceChecks_BusinessId] ON [EmailBounceChecks] ([BusinessId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818104226_AddEmailValidationPipeline'
)
BEGIN
    CREATE INDEX [IX_EmailBounceChecks_Status_SentAt] ON [EmailBounceChecks] ([Status], [SentAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818104226_AddEmailValidationPipeline'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260818104226_AddEmailValidationPipeline', N'8.0.11');
END;
GO

COMMIT;
GO

