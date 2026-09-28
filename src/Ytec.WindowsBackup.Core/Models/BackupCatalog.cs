namespace Ytec.WindowsBackup.Core.Models;

public sealed class BackupCatalog
{
    public int SchemaVersion { get; init; }

    public string CatalogVersion { get; init; } = string.Empty;

    public List<string> ExcludedFileNames { get; init; } = [];

    public List<BackupItemDefinition> Items { get; init; } = [];
}

public sealed class BackupItemDefinition
{
    public string Id { get; init; } = string.Empty;

    public BackupItemKind Kind { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string DisplayNameEn { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string DescriptionEn { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string CategoryEn { get; init; } = string.Empty;

    public BackupScope Scope { get; init; }

    public BackupSelectionMode SelectionMode { get; init; }

    public List<string> Paths { get; init; } = [];

    public List<string> Extensions { get; init; } = [];

    public List<string> ExcludedFileNames { get; init; } = [];

    public string? DestinationFolder { get; init; }

    public Dictionary<string, string> ExtensionDestinationFolders { get; init; } = [];

    public bool DefaultSelected { get; init; }

    public string? LegacyEquivalent { get; init; }
}

public enum BackupScope
{
    PerUser,
    SourceRoot,
}

public enum BackupItemKind
{
    FileCopy,
    WifiProfiles,
}

public enum BackupSelectionMode
{
    AllFiles,
    FilesOnly,
    ExtensionsOnly,
}
