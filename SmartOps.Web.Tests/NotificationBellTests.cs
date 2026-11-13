using System;
using System.Linq;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Components.Layout;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

/// <summary>bUnit de la campana (docs/redesign/07-notificaciones.html): badge 99+, abrir, marcar leída, Escape, Analizar.</summary>
public sealed class NotificationBellTests : IDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly AppDbContext _db;
    private readonly TransactionDrawerService _drawer = new(NullLogger<TransactionDrawerService>.Instance);

    public NotificationBellTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("bell-" + Guid.NewGuid()).Options);

        var now = DateTime.UtcNow;
        for (var i = 1; i <= 106; i++)
        {
            _db.Transactions.Add(new Transaction
            {
                Id = i, Amount = 10m, Currency = "USD", CardLast4 = "3723", Status = "Failed",
                ErrorMessage = "Suspected fraud", Provider = "Simulator", OccurredAt = now
            });
            _db.Notifications.Add(new Notification
            {
                Id = i, TransactionId = i, Title = $"Transacción {i} falló", Message = "Suspected fraud",
                Type = "Error", IsRead = i == 1, CreatedAt = now.AddMinutes(-(106 - i))
            });
        }
        _db.SaveChanges();

        _ctx.Services.AddSingleton(_db);
        _ctx.Services.AddSingleton(_drawer);
        _ctx.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        _ctx.Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _db.Dispose();
    }

    private IRenderedComponent<NotificationBell> RenderOpen()
    {
        var cut = _ctx.Render<NotificationBell>();
        cut.Find("#notification-bell-btn").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#notification-panel")));
        return cut;
    }

    [Fact]
    public void Badge_CapsAt99_AriaLabelHasExactNumber()
    {
        var cut = _ctx.Render<NotificationBell>();
        var bell = cut.Find("#notification-bell-btn");

        Assert.Equal("99+", cut.Find(".notif-badge").TextContent);
        Assert.Equal("Notificaciones, 105 sin leer", bell.GetAttribute("aria-label"));
        Assert.Equal("false", bell.GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("#notification-panel"));
        Assert.DoesNotContain("style=", cut.Markup);
    }

    [Fact]
    public void Open_ShowsHeaderCount_ItemsAndActions()
    {
        var cut = RenderOpen();

        Assert.Equal("true", cut.Find("#notification-bell-btn").GetAttribute("aria-expanded"));
        Assert.Equal("105 sin leer", cut.Find(".notif-count").TextContent);

        var newest = cut.Find("li[data-id='106']");
        Assert.Contains("is-unread", newest.ClassName);
        Assert.Equal("Falló la TX 106", newest.QuerySelector(".notif-item-title")!.TextContent.Trim());
        Assert.Equal("TX 106", newest.QuerySelector(".notif-item-title .mono")!.TextContent);
        Assert.Equal("Suspected fraud · •••• 3723", newest.QuerySelector(".notif-meta")!.TextContent);
        Assert.Equal("ahora", newest.QuerySelector(".notif-ago")!.TextContent);
        Assert.NotNull(newest.QuerySelector(".notif-analyze"));
        Assert.NotNull(newest.QuerySelector(".notif-mark-read"));
    }

    [Fact]
    public void MarkAsRead_UpdatesItemAndCounts()
    {
        var cut = RenderOpen();

        cut.Find("li[data-id='106'] .notif-mark-read").Click();

        cut.WaitForAssertion(() => Assert.Equal("104 sin leer", cut.Find(".notif-count").TextContent));
        var item = cut.Find("li[data-id='106']");
        Assert.Contains("is-read", item.ClassName);
        Assert.Null(item.QuerySelector(".notif-mark-read"));
        Assert.NotNull(item.QuerySelector(".notif-analyze"));
        Assert.Equal("Notificaciones, 104 sin leer", cut.Find("#notification-bell-btn").GetAttribute("aria-label"));
        Assert.True(_db.Notifications.Single(n => n.Id == 106).IsRead);
    }

    [Fact]
    public void MarkAll_ClearsBadge()
    {
        var cut = RenderOpen();

        cut.Find(".notif-mark-all").Click();

        cut.WaitForAssertion(() => Assert.Equal("0 sin leer", cut.Find(".notif-count").TextContent));
        Assert.Empty(cut.FindAll(".notif-badge"));
        Assert.True(cut.Find(".notif-mark-all").HasAttribute("disabled"));
    }

    [Fact]
    public void Escape_ClosesPanel()
    {
        var cut = RenderOpen();

        cut.Find("#notification-panel").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("#notification-panel"));
        Assert.Equal("false", cut.Find("#notification-bell-btn").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void ClickOutside_ClosesPanel()
    {
        var cut = RenderOpen();
        cut.Find(".notif-backdrop").Click();
        Assert.Empty(cut.FindAll("#notification-panel"));
    }

    [Fact]
    public void Analizar_OpensDrawer_AndClosesPanel()
    {
        var cut = RenderOpen();

        cut.Find("li[data-id='106'] .notif-analyze").Click();

        cut.WaitForAssertion(() => Assert.Equal(106, _drawer.Current?.Id));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#notification-panel")));
    }
}
