using System.Net;
using System.Text;

namespace Migrator.Lab.Reports;

internal static class LabHtml
{
    public static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    public static string Page(string title, string body)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.AppendLine($"<title>{Encode(title)}</title>");
        builder.AppendLine("""
<style>
:root{font-family:Inter,Segoe UI,Arial,sans-serif;color-scheme:light dark;--bg:#f5f7fb;--card:#fff;--text:#172033;--muted:#657087;--line:#dbe1ea;--ok:#16794b;--bad:#b42318;--warn:#9a6700;--info:#175cd3}
@media(prefers-color-scheme:dark){:root{--bg:#10131a;--card:#171c25;--text:#edf2f7;--muted:#a6b0c3;--line:#303847;--ok:#5fd39a;--bad:#ff8a80;--warn:#ffd166;--info:#84adff}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text)}main{max-width:1440px;margin:auto;padding:28px}h1,h2,h3{margin-top:0}a{color:var(--info)}.meta{color:var(--muted);margin-bottom:20px}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:12px;margin:18px 0}.card{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:14px}.card strong{display:block;font-size:1.5rem}.table-wrap{overflow:auto;background:var(--card);border:1px solid var(--line);border-radius:12px}table{width:100%;border-collapse:collapse;min-width:880px}th,td{text-align:left;padding:10px 12px;border-bottom:1px solid var(--line);vertical-align:top}th{position:sticky;top:0;background:var(--card)}tr:last-child td{border-bottom:0}.status{font-weight:700}.ok{color:var(--ok)}.bad{color:var(--bad)}.warn{color:var(--warn)}.muted{color:var(--muted)}details{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:10px 12px;margin:10px 0}code{white-space:pre-wrap;word-break:break-word}.delta-pos{color:var(--bad)}.delta-neg{color:var(--ok)}
</style>
""");
        builder.AppendLine("</head><body><main>");
        builder.AppendLine(body);
        builder.AppendLine("</main></body></html>");
        return builder.ToString();
    }
}
