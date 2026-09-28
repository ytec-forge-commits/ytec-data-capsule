using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Ytec.WindowsBackup.Core.Models;

namespace Ytec.WindowsBackup.Core.Services;

public static class WifiEncryptedContainer
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("YTECWIFI");
    private static readonly byte[] KeyDerivationLabel =
        Encoding.ASCII.GetBytes("Y-TEC Windows Backup Wi-Fi container v2");

    private const byte FormatVersion = 2;
    private const byte ApplicationKeyId = 1;
    private const int MasterKeySize = 32;
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int TagSize = 32;
    private const int HeaderSize = 8 + 1 + 1 + SaltSize + IvSize + sizeof(int);
    private const int MaximumProfiles = 256;
    private const int MaximumPlaintextBytes = 24 * 1024 * 1024;
    private const int MaximumCombinedProfileBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        DateParseHandling = DateParseHandling.DateTimeOffset,
    };

    public static byte[] Encrypt(
        byte[] masterKey,
        IReadOnlyList<byte[]> profiles,
        DateTimeOffset createdAt)
    {
        ValidateMasterKey(masterKey);
        ValidateProfiles(profiles);

        var payload = Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(
                new WifiPayload
                {
                    SchemaVersion = 1,
                    CreatedAt = createdAt,
                    Profiles = profiles.Select(profile => (byte[])profile.Clone()).ToList(),
                },
                Formatting.None,
                SerializerSettings));
        if (payload.Length > MaximumPlaintextBytes)
        {
            ZeroMemory(payload);
            throw new InvalidDataException("Wi-Fiバックアップの暗号化前サイズが上限を超えています。");
        }

        var salt = RandomBytes(SaltSize);
        var iv = RandomBytes(IvSize);
        var encryptionKey = DeriveKey(masterKey, salt, 1);
        var authenticationKey = DeriveKey(masterKey, salt, 2);
        byte[]? ciphertext = null;
        try
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encryptionKey;
                aes.IV = iv;
                using var encryptor = aes.CreateEncryptor();
                ciphertext = encryptor.TransformFinalBlock(payload, 0, payload.Length);
            }

            if (ciphertext.Length > MaximumPlaintextBytes + 32)
            {
                throw new InvalidDataException("Wi-Fiバックアップの暗号化後サイズが上限を超えています。");
            }

            var container = new byte[HeaderSize + ciphertext.Length + TagSize];
            Buffer.BlockCopy(Magic, 0, container, 0, Magic.Length);
            container[8] = FormatVersion;
            container[9] = ApplicationKeyId;
            Buffer.BlockCopy(salt, 0, container, 10, salt.Length);
            Buffer.BlockCopy(iv, 0, container, 10 + SaltSize, iv.Length);
            WriteInt32LittleEndian(
                container,
                HeaderSize - sizeof(int),
                ciphertext.Length);
            Buffer.BlockCopy(ciphertext, 0, container, HeaderSize, ciphertext.Length);

            using (var hmac = new HMACSHA256(authenticationKey))
            {
                var tag = hmac.ComputeHash(container, 0, HeaderSize + ciphertext.Length);
                Buffer.BlockCopy(
                    tag,
                    0,
                    container,
                    HeaderSize + ciphertext.Length,
                    TagSize);
                ZeroMemory(tag);
            }

            return container;
        }
        finally
        {
            ZeroMemory(payload);
            ZeroMemory(salt);
            ZeroMemory(iv);
            ZeroMemory(encryptionKey);
            ZeroMemory(authenticationKey);
            if (ciphertext is not null)
            {
                ZeroMemory(ciphertext);
            }
        }
    }

    public static WifiContainerContents Decrypt(byte[] masterKey, byte[] container)
    {
        ValidateMasterKey(masterKey);
        if (container is null)
        {
            throw new ArgumentNullException(nameof(container));
        }

        if (container.Length < HeaderSize + TagSize ||
            !MatchesAt(container, 0, Magic) ||
            container[8] != FormatVersion ||
            container[9] != ApplicationKeyId)
        {
            throw new InvalidDataException("対応していないWi-Fiバックアップ形式です。");
        }

        var ciphertextLength = ReadInt32LittleEndian(
            container,
            HeaderSize - sizeof(int));
        if (ciphertextLength <= 0 ||
            ciphertextLength > MaximumPlaintextBytes + 32 ||
            container.Length != HeaderSize + ciphertextLength + TagSize)
        {
            throw new InvalidDataException("Wi-Fiバックアップの長さが不正です。");
        }

        var salt = new byte[SaltSize];
        var iv = new byte[IvSize];
        Buffer.BlockCopy(container, 10, salt, 0, SaltSize);
        Buffer.BlockCopy(container, 10 + SaltSize, iv, 0, IvSize);
        var encryptionKey = DeriveKey(masterKey, salt, 1);
        var authenticationKey = DeriveKey(masterKey, salt, 2);
        byte[]? plaintext = null;
        try
        {
            byte[] expectedTag;
            using (var hmac = new HMACSHA256(authenticationKey))
            {
                expectedTag = hmac.ComputeHash(
                    container,
                    0,
                    HeaderSize + ciphertextLength);
            }

            var actualTag = new byte[TagSize];
            Buffer.BlockCopy(
                container,
                HeaderSize + ciphertextLength,
                actualTag,
                0,
                TagSize);
            var authentic = FixedTimeEquals(expectedTag, actualTag);
            ZeroMemory(expectedTag);
            ZeroMemory(actualTag);
            if (!authentic)
            {
                throw new InvalidDataException(
                    "Wi-Fiバックアップを認証できません。破損または異なるアプリ鍵の可能性があります。");
            }

            var ciphertext = new byte[ciphertextLength];
            Buffer.BlockCopy(container, HeaderSize, ciphertext, 0, ciphertextLength);
            try
            {
                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encryptionKey;
                aes.IV = iv;
                using var decryptor = aes.CreateDecryptor();
                plaintext = decryptor.TransformFinalBlock(
                    ciphertext,
                    0,
                    ciphertext.Length);
            }
            finally
            {
                ZeroMemory(ciphertext);
            }

            WifiPayload? payload;
            try
            {
                payload = JsonConvert.DeserializeObject<WifiPayload>(
                    Encoding.UTF8.GetString(plaintext),
                    SerializerSettings);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Wi-Fiバックアップの内容が不正です。", exception);
            }

            if (payload is null)
            {
                throw new InvalidDataException("Wi-Fiバックアップの内容がありません。");
            }

            if (payload.SchemaVersion != 1)
            {
                throw new InvalidDataException(
                    $"未対応のWi-Fi内容スキーマです: {payload.SchemaVersion}");
            }

            ValidateProfiles(payload.Profiles);
            return new WifiContainerContents(
                payload.CreatedAt,
                payload.Profiles.Select(profile => (byte[])profile.Clone()).ToArray());
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException(
                "Wi-Fiバックアップを復号できません。破損または異なるアプリ鍵の可能性があります。",
                exception);
        }
        finally
        {
            ZeroMemory(salt);
            ZeroMemory(iv);
            ZeroMemory(encryptionKey);
            ZeroMemory(authenticationKey);
            if (plaintext is not null)
            {
                ZeroMemory(plaintext);
            }
        }
    }

    private static void ValidateMasterKey(byte[] masterKey)
    {
        if (masterKey is null)
        {
            throw new ArgumentNullException(nameof(masterKey));
        }

        if (masterKey.Length != MasterKeySize)
        {
            throw new ArgumentException(
                "Wi-Fi暗号化マスター鍵は32バイトである必要があります。",
                nameof(masterKey));
        }
    }

    private static void ValidateProfiles(IReadOnlyList<byte[]> profiles)
    {
        if (profiles is null)
        {
            throw new ArgumentNullException(nameof(profiles));
        }

        if (profiles.Count <= 0 || profiles.Count > MaximumProfiles)
        {
            throw new InvalidDataException("Wi-Fiプロファイル数が上限範囲外です。");
        }

        var combinedBytes = 0L;
        foreach (var profile in profiles)
        {
            if (profile is null)
            {
                throw new InvalidDataException("Wi-Fiプロファイルが空です。");
            }

            WifiProfileXmlValidator.Validate(profile);
            combinedBytes += profile.Length;
            if (combinedBytes > MaximumCombinedProfileBytes)
            {
                throw new InvalidDataException("Wi-Fiプロファイルの合計サイズが上限を超えています。");
            }
        }
    }

    private static byte[] DeriveKey(
        byte[] masterKey,
        byte[] salt,
        byte purpose)
    {
        var material = new byte[KeyDerivationLabel.Length + 1 + salt.Length];
        Buffer.BlockCopy(KeyDerivationLabel, 0, material, 0, KeyDerivationLabel.Length);
        material[KeyDerivationLabel.Length] = purpose;
        Buffer.BlockCopy(
            salt,
            0,
            material,
            KeyDerivationLabel.Length + 1,
            salt.Length);
        try
        {
            using var hmac = new HMACSHA256(masterKey);
            return hmac.ComputeHash(material);
        }
        finally
        {
            ZeroMemory(material);
        }
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        using var generator = RandomNumberGenerator.Create();
        generator.GetBytes(bytes);
        return bytes;
    }

    private static bool MatchesAt(byte[] source, int offset, byte[] expected)
    {
        if (source.Length - offset < expected.Length)
        {
            return false;
        }

        var difference = 0;
        for (var index = 0; index < expected.Length; index++)
        {
            difference |= source[offset + index] ^ expected[index];
        }

        return difference == 0;
    }

    private static bool FixedTimeEquals(byte[] first, byte[] second)
    {
        if (first.Length != second.Length)
        {
            return false;
        }

        var difference = 0;
        for (var index = 0; index < first.Length; index++)
        {
            difference |= first[index] ^ second[index];
        }

        return difference == 0;
    }

    private static void WriteInt32LittleEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static int ReadInt32LittleEndian(byte[] buffer, int offset) =>
        buffer[offset] |
        (buffer[offset + 1] << 8) |
        (buffer[offset + 2] << 16) |
        (buffer[offset + 3] << 24);

    private static void ZeroMemory(byte[] bytes)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = 0;
        }
    }

    private sealed class WifiPayload
    {
        public int SchemaVersion { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public List<byte[]> Profiles { get; set; } = new();
    }
}
