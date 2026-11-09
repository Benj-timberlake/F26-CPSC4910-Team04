using Microsoft.JSInterop;

namespace FrontEnd.Shared;

// builds and saves a pdf in the browser through wwwroot/js/reports.js
public static class PdfReport
{
    public static async Task Save(IJSRuntime js, string title, string subtitle, string[] columns,
        IEnumerable<string[]> rows, string filename)
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/reports.js");
        await module.InvokeVoidAsync("pdf", new { title, subtitle, columns, rows = rows.ToArray(), filename });
    }

    // several headed tables in one file
    public static async Task Save(IJSRuntime js, string title, string subtitle, IEnumerable<PdfSection> sections, string filename)
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/reports.js");
        await module.InvokeVoidAsync("pdf", new { title, subtitle, sections = sections.ToArray(), filename });
    }

    public static string Generated() => $"generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
}

public sealed record PdfSection(string Heading, string[] Columns, string[][] Rows);
