using System.Text;
using Lapse.Cli.Output;

namespace Lapse.Cli.Tests;

public class HtmlReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Item_names_cannot_break_out_of_the_embedded_data()
    {
        var html = Render(new ReportRow("ok", "tls", "</script><script>alert(1)</script>", "it", Now.AddDays(10), null));

        Assert.DoesNotContain("</script><script>alert(1)", html, StringComparison.Ordinal);
        Assert.Single(html.Split("<script id=\"lapse-data\"")[1..]);
    }

    [Fact]
    public void Report_embeds_dates_and_days_remaining()
    {
        var html = Render(new ReportRow("warning", "domain", "example.com", "it", Now.AddDays(20).AddHours(1), "new"));

        Assert.Contains("\"expires\":\"2026-10-17\"", html, StringComparison.Ordinal);
        Assert.Contains("\"days\":20", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__LAPSE_DATA__", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_loads_nothing_from_the_network()
    {
        var html = Render();

        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<link", html, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"http", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_files_can_be_turned_into_a_report()
    {
        var rows = HtmlReport.FromExport("""
            [ { "kind": "tls", "key": "a:443", "name": "a:443", "owner": "it", "expiresAt": "2026-10-10T00:00:00+00:00", "daysRemaining": 12, "status": "warning", "note": null },
              { "kind": "domain", "key": "b.com", "name": "b.com", "owner": "it", "expiresAt": null, "daysRemaining": null, "status": "error", "note": "no response" } ]
            """);

        Assert.True(rows.IsSuccess, rows.Error);
        Assert.Equal(["warning", "error"], rows.Value.Select(row => row.Status));
        Assert.Null(rows.Value[1].ExpiresAt);
    }

    [Fact]
    public void Other_json_files_are_rejected() =>
        Assert.Equal("the file was not created by lapse export", HtmlReport.FromExport("""{ "owners": {} }""").Error);

    private static string Render(params ReportRow[] rows)
    {
        using var buffer = new MemoryStream();
        HtmlReport.Write(buffer, rows, Now);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
