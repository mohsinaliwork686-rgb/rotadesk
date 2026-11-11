namespace Rotadesk.Domain;

/// <summary>One window of usual availability, before it's saved.</summary>
public readonly record struct DayWindow(DayOfWeek Day, TimeOnly From, TimeOnly To);

public static class AvailabilityWindows
{
    /// <summary>
    /// Merges windows that overlap or touch on the same day. The rules look for one window that covers a whole shift,
    /// so 09:00–13:00 and 13:00–17:00 kept apart would flag a 10:00–16:00 shift that is really fine.
    /// Returned Monday first, then by start time.
    /// </summary>
    public static IReadOnlyList<DayWindow> Merge(IEnumerable<DayWindow> windows)
    {
        var merged = new List<DayWindow>();
        foreach (var day in windows.GroupBy(w => w.Day).OrderBy(g => ((int)g.Key + 6) % 7))
        {
            DayWindow? open = null;
            foreach (var window in day.OrderBy(w => w.From))
            {
                if (window.To <= window.From)
                {
                    throw new DomainException("availability.invalid-times", "An availability window has to end after it starts, on the same day.");
                }

                if (open is { } current && window.From <= current.To)
                {
                    open = current with { To = window.To > current.To ? window.To : current.To };
                }
                else
                {
                    if (open is { } done)
                    {
                        merged.Add(done);
                    }

                    open = window;
                }
            }

            if (open is { } last)
            {
                merged.Add(last);
            }
        }

        return merged;
    }
}
