using System;

namespace SmartOps.Web.Services;

/// <summary>Formatos del desplegable de notificaciones (docs/redesign/07-notificaciones.html).</summary>
public static class NotificationFormat
{
    /// <summary>Badge de la campana: no leídas, con tope «99+». Null cuando no hay ninguna (sin badge).</summary>
    public static string? Badge(int unread) => unread switch
    {
        <= 0 => null,
        > 99 => "99+",
        _ => unread.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    /// <summary>aria-label de la campana, siempre con el número exacto.</summary>
    public static string AriaLabel(int unread) => unread switch
    {
        <= 0 => "Notificaciones, ninguna sin leer",
        _ => $"Notificaciones, {unread} sin leer"
    };

    /// <summary>«ahora», «hace N min», «hace N h» y, desde las 24 h, «dd/MM HH:mm» (hora local, como el Historial).</summary>
    public static string RelativeTime(DateTime createdAtUtc, DateTime nowUtc)
    {
        var created = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
        var span = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc) - created;
        if (span < TimeSpan.FromMinutes(1)) return "ahora";
        if (span < TimeSpan.FromHours(1)) return $"hace {(int)span.TotalMinutes} min";
        if (span < TimeSpan.FromHours(24)) return $"hace {(int)span.TotalHours} h";
        return HistoryFormat.ShortDate(created);
    }
}
