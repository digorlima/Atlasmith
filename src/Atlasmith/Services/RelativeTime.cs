using Atlasmith.Localization;

namespace Atlasmith.Services;

/// <summary>"há 5 min", "ontem", "em 03/10/2026" (ou o equivalente em inglês): para datas que só precisam ser situadas.</summary>
public static class RelativeTime
{
    public static string Describe(DateTime when) => Describe(when, DateTime.Now);

    public static string Describe(DateTime when, DateTime now)
    {
        var t = Texts.Current;
        var elapsed = now - when;

        // Negativo cobre relógio adiantado em outro PC (arquivo vindo pela rede ou pelo git).
        if (elapsed < TimeSpan.FromMinutes(1)) return t.JustNow;
        if (elapsed < TimeSpan.FromHours(1)) return t.MinutesAgo((int)elapsed.TotalMinutes);
        if (elapsed < TimeSpan.FromHours(12)) return t.HoursAgo((int)elapsed.TotalHours);

        int days = (now.Date - when.Date).Days;
        if (days == 0) return t.Today;
        if (days == 1) return t.Yesterday;
        if (days < 7) return t.DaysAgo(days);

        return t.OnDate(when);
    }
}
