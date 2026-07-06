using System.Threading.Tasks;

namespace Application.Interfaces;

/// <summary>
/// Абстракция над сервисом шифрования.
/// Используется для защиты чувствительных данных перед сохранением в БД.
/// </summary>
public interface IEncryptionService
{
    /// <summary>Зашифровать строку.</summary>
    string Encrypt(string plainText);

    /// <summary>Расшифровать строку.</summary>
    string Decrypt(string cipherText);
}