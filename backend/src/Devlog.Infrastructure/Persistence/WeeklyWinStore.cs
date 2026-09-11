using System.Text.Json;
using Dapper;
using Devlog.Core.Abstractions;
using Devlog.Core.Domain;

namespace Devlog.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of IWeeklyWinStore. Keyed on (week_from, week_to).
/// </summary>
public sealed class WeeklyWinStore(ISqliteConnectionFactory factory) : IWeeklyWinStore
{
    public async Task<WeeklyWin?> GetAsync(DateOnly weekFrom, DateOnly weekTo, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<WeeklyWinRow>(new CommandDefinition(
            """
            SELECT week_from, week_to, summary, highlights,
                   narrative_count, narratives_max_generated_utc, model, generated_utc
            FROM weekly_win
            WHERE week_from = @weekFrom AND week_to = @weekTo;
            """,
            new { weekFrom = weekFrom.ToString("yyyy-MM-dd"), weekTo = weekTo.ToString("yyyy-MM-dd") },
            cancellationToken: ct)).ConfigureAwait(false);

        return row?.ToDomain();
    }

    public async Task<List<WeeklyWin>> GetRangeAsync(DateOnly monthFrom, DateOnly monthTo, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<WeeklyWinRow>(new CommandDefinition(
            """
            SELECT week_from, week_to, summary, highlights,
                   narrative_count, narratives_max_generated_utc, model, generated_utc
            FROM weekly_win
            WHERE week_from >= @monthFrom AND week_to <= @monthTo
            ORDER BY week_from ASC;
            """,
            new { monthFrom = monthFrom.ToString("yyyy-MM-dd"), monthTo = monthTo.ToString("yyyy-MM-dd") },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task UpsertAsync(WeeklyWin win, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        var highlightsJson = JsonSerializer.Serialize(win.Highlights);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO weekly_win
              (week_from, week_to, summary, highlights,
               narrative_count, narratives_max_generated_utc, model, generated_utc)
            VALUES
              (@WeekFrom, @WeekTo, @Summary, @HighlightsJson,
               @NarrativeCount, @NarrativesMaxGeneratedUtc, @Model, @GeneratedUtc)
            ON CONFLICT (week_from, week_to) DO UPDATE SET
              summary                      = excluded.summary,
              highlights                   = excluded.highlights,
              narrative_count              = excluded.narrative_count,
              narratives_max_generated_utc = excluded.narratives_max_generated_utc,
              model                        = excluded.model,
              generated_utc                = excluded.generated_utc;
            """,
            new
            {
                WeekFrom = win.WeekFrom.ToString("yyyy-MM-dd"),
                WeekTo = win.WeekTo.ToString("yyyy-MM-dd"),
                win.Summary,
                HighlightsJson = highlightsJson,
                win.NarrativeCount,
                win.NarrativesMaxGeneratedUtc,
                win.Model,
                win.GeneratedUtc
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private sealed class WeeklyWinRow
    {
        public string week_from { get; set; } = string.Empty;
        public string week_to { get; set; } = string.Empty;
        public string summary { get; set; } = string.Empty;
        public string highlights { get; set; } = "[]";
        public int narrative_count { get; set; }
        public long narratives_max_generated_utc { get; set; }
        public string model { get; set; } = string.Empty;
        public long generated_utc { get; set; }

        public WeeklyWin ToDomain()
        {
            List<string> parsedHighlights;
            try
            {
                parsedHighlights = JsonSerializer.Deserialize<List<string>>(highlights) ?? [];
            }
            catch
            {
                parsedHighlights = [];
            }

            return new WeeklyWin
            {
                WeekFrom = DateOnly.Parse(week_from),
                WeekTo = DateOnly.Parse(week_to),
                Summary = summary,
                Highlights = parsedHighlights,
                NarrativeCount = narrative_count,
                NarrativesMaxGeneratedUtc = narratives_max_generated_utc,
                Model = model,
                GeneratedUtc = generated_utc
            };
        }
    }
}
