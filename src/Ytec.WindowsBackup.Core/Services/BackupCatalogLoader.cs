using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Ytec.WindowsBackup.Core.Models;

namespace Ytec.WindowsBackup.Core.Services;

public sealed class BackupCatalogLoader
{
    private static readonly Regex ItemIdPattern =
        new(
            "^[a-z0-9]+(?:-[a-z0-9]+)*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        Converters = { new StringEnumConverter() },
    };

    public BackupCatalog Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("設定ファイルを指定してください。", nameof(path));
        }

        BackupCatalog? catalog;
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            using var jsonReader = new JsonTextReader(reader)
            {
                MaxDepth = 32,
            };
            var serializer = JsonSerializer.Create(SerializerSettings);
            catalog = serializer.Deserialize<BackupCatalog>(jsonReader);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("バックアップ項目設定を解析できませんでした。", exception);
        }

        if (catalog is null)
        {
            throw new InvalidDataException("バックアップ項目設定を読み取れませんでした。");
        }

        Validate(catalog);
        return catalog;
    }

    public void Validate(BackupCatalog catalog)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }
        if (catalog.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"未対応のバックアップ項目スキーマです: {catalog.SchemaVersion}");
        }

        if (string.IsNullOrWhiteSpace(catalog.CatalogVersion))
        {
            throw new InvalidDataException("catalogVersion は必須です。");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog.Items)
        {
            if (!ItemIdPattern.IsMatch(item.Id))
            {
                throw new InvalidDataException($"項目IDが不正です: {item.Id}");
            }

            if (!ids.Add(item.Id))
            {
                throw new InvalidDataException($"項目IDが重複しています: {item.Id}");
            }

            if (string.IsNullOrWhiteSpace(item.DisplayName) ||
                string.IsNullOrWhiteSpace(item.Category))
            {
                throw new InvalidDataException($"表示名またはカテゴリーが空です: {item.Id}");
            }

            if (string.IsNullOrWhiteSpace(item.DisplayNameEn) ||
                string.IsNullOrWhiteSpace(item.DescriptionEn) ||
                string.IsNullOrWhiteSpace(item.CategoryEn))
            {
                throw new InvalidDataException(
                    $"英語の表示名、説明またはカテゴリーが空です: {item.Id}");
            }

            if (item.Kind == BackupItemKind.FileCopy && item.Paths.Count == 0)
            {
                throw new InvalidDataException($"paths が空です: {item.Id}");
            }

            if (item.Kind != BackupItemKind.FileCopy &&
                (item.Paths.Count > 0 || item.Extensions.Count > 0))
            {
                throw new InvalidDataException(
                    $"OS機能項目にファイルパスや拡張子は指定できません: {item.Id}");
            }

            foreach (var path in item.Paths)
            {
                BackupPathPolicy.EnsureSafeRelativeDefinitionPath(path);
            }

            if (item.SelectionMode == BackupSelectionMode.ExtensionsOnly &&
                item.Extensions.Count == 0)
            {
                throw new InvalidDataException($"拡張子検索のextensionsが空です: {item.Id}");
            }

            foreach (var extension in item.Extensions)
            {
                if (!extension.StartsWith(".", StringComparison.Ordinal) ||
                    extension.IndexOfAny(['\\', '/', '*', '?']) >= 0)
                {
                    throw new InvalidDataException($"拡張子が不正です: {item.Id} / {extension}");
                }
            }

            foreach (var excludedName in item.ExcludedFileNames)
            {
                ValidateExcludedFileName(excludedName, item.Id);
            }

            if (!string.IsNullOrWhiteSpace(item.DestinationFolder))
            {
                ValidateDestinationFolder(item.Id, item.DestinationFolder!);
            }

            foreach (var route in item.ExtensionDestinationFolders)
            {
                if (!item.Extensions.Contains(
                        route.Key,
                        StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"出力先を指定した拡張子がextensionsにありません: " +
                        $"{item.Id} / {route.Key}");
                }

                ValidateDestinationFolder(item.Id, route.Value);
            }
        }

        foreach (var excludedName in catalog.ExcludedFileNames)
        {
            ValidateExcludedFileName(excludedName, "全項目");
        }
    }

    private static void ValidateExcludedFileName(string excludedName, string itemId)
    {
        if (string.IsNullOrWhiteSpace(excludedName) ||
            excludedName.IndexOfAny(['\\', '/', '*', '?']) >= 0 ||
            !string.Equals(
                excludedName,
                Path.GetFileName(excludedName),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"除外ファイル名が不正です: {itemId} / {excludedName}");
        }
    }

    private static void ValidateDestinationFolder(string itemId, string folder)
    {
        BackupPathPolicy.EnsureSafeRelativeDefinitionPath(folder);
        if (folder.IndexOfAny(['*', '?']) >= 0)
        {
            throw new InvalidDataException(
                $"出力先フォルダーにワイルドカードは使用できません: {itemId} / {folder}");
        }
    }

}
