namespace Application.Interfaces;

/// <summary>
/// Абстракция над генератором случайных чисел.
/// Используется для добавления Jitter к интервалам повторных попыток,
/// чтобы избежать эффекта "гремящего стада" (Thundering Herd). 
/// </summary>
public interface IRandomProvider
{
    /// <summary>
    /// Возвращает случайное целое число в диапазоне [minValue, maxValue).
    /// </summary>
    /// <param name="minValue">Нижняя граница (включительно).</param>
    /// <param name="maxValue">Верхняя граница (не включительно).</param>
    int Next(int minValue, int maxValue);
}