using ZarobitokApp.Models;

namespace ZarobitokApp.Services;

/// <summary>
/// Швидкий підрахунок допзаробітку по проміжках часу. Тіки сортуються один раз,
/// ціни складаються у словник один раз, а межі проміжку шукаються бінарним пошуком.
/// Для одного виклику EarningsCalculator.TicksTotal простіший, але коли треба
/// порахувати десятки змін (журнал, календар, статистика) — тут це O(log n) на зміну,
/// а не повний прохід по всіх тіках зі збіркою словника щоразу.
/// </summary>
public sealed class TicksIndex
{
    private readonly ExtraTick[] _sorted;
    private readonly DateTime[] _times;
    private readonly Dictionary<Guid, decimal> _priceById;

    public TicksIndex(IEnumerable<ExtraItem> items, IEnumerable<ExtraTick> ticks)
    {
        _priceById = items.ToDictionary(i => i.Id, i => i.UnitPrice);
        _sorted = ticks.OrderBy(t => t.AtUtc).ToArray();
        _times = _sorted.Select(t => t.AtUtc).ToArray();
    }

    /// <summary>Сума тіків у проміжку [fromUtc, toUtc] — той самий результат, що й TicksTotal.</summary>
    public decimal Total(DateTime fromUtc, DateTime toUtc)
    {
        var sum = 0m;
        for (var i = LowerBound(fromUtc); i < _sorted.Length && _times[i] <= toUtc; i++)
            sum += EarningsCalculator.TickValue(_sorted[i], _priceById);
        return sum;
    }

    // Перший індекс із часом >= value.
    private int LowerBound(DateTime value)
    {
        int lo = 0, hi = _times.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_times[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
