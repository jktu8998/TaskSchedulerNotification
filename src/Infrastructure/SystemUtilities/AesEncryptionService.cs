// Infrastructure/System/AesEncryptionService.cs
using System;
using System.Security.Cryptography;
using System.Text; // <-- Подключенный неймспейс
using Application.Interfaces;

namespace Infrastructure.SystemUtilities;

/// <summary>
/// Сервис шифрования на основе AES-256.
/// Ключ получает через конструктор (из безопасного хранилища или переменной окружения).
/// IV генерируется случайно и сохраняется вместе с шифротекстом (первые 16 байт).
/// </summary>
public sealed class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public AesEncryptionService(string base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new ArgumentException("Encryption key cannot be empty.", nameof(base64Key));

        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32) // AES-256 требует ровно 32 байта
            throw new ArgumentException("Key must be exactly 32 bytes for AES-256.", nameof(base64Key));
    }

    public string Encrypt(string plainText)
    {
        // Защита от пустых значений
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV(); // Генерируем новый IV для каждого шифрования

        using var encryptor = aes.CreateEncryptor();
        
        // Исправлено: убрали System.Text, так как есть using сверху
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Формируем результат: IV + шифротекст
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = _key;

        // Извлекаем IV (первые 16 байт)
        var iv = new byte[16];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        aes.IV = iv;

        // Извлекаем сам шифротекст
        var cipherBytes = new byte[fullCipher.Length - iv.Length];
        Buffer.BlockCopy(fullCipher, iv.Length, cipherBytes, 0, cipherBytes.Length);

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        // Исправлено: убрали System.Text
        return Encoding.UTF8.GetString(plainBytes);
    }
}