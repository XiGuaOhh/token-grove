using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TokenGrove {
  static class ClaudeUsageRegressionTests {
    static string Reply(DateTime time, string sessionId, string messageId, string entryId,
        int output, string stopReason = "end_turn", string model = "claude-opus-test",
        string requestId = null) {
      return J.Serializer().Serialize(new {
        type = "assistant",
        timestamp = J.Iso(time),
        sessionId,
        uuid = entryId,
        requestId,
        message = new {
          id = messageId,
          model,
          stop_reason = stopReason,
          usage = new {
            input_tokens = 7,
            output_tokens = output,
            cache_read_input_tokens = 450000,
            cache_creation_input_tokens = 2500
          }
        }
      }) + "\n";
    }

    public static void Run(string root, Action<bool, string> check) {
      var time = new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);
      var measured = time.AddMinutes(5);
      string original = Path.Combine(root, "original.jsonl");
      string fork = Path.Combine(root, "fork.jsonl");
      var paths = new Dictionary<string, string> { [original] = "claude", [fork] = "claude" };
      File.WriteAllText(original, Reply(time, "original", "msg_replayed", "entry-a", 17));
      File.WriteAllText(fork, Reply(time, "fork", "msg_replayed", "entry-b", 17));

      var collector = new DataCollector();
      var rows = collector.ReadUsageFiles(paths);
      const long requestTokens = 452524;
      var query = UsageEngine.Query(rows, time.AddMinutes(-1), measured, "claude");
      check(query.records == 1 && query.totals.total == requestTokens,
        "A Claude request replayed into a different session is counted once by the local usage index");

      File.AppendAllText(fork, Reply(time, "fork", "msg_independent", "entry-c", 17));
      rows = collector.ReadUsageFiles(paths);
      query = UsageEngine.Query(rows, time.AddMinutes(-1), measured, "claude");
      check(query.records == 2 && query.totals.total == 2 * requestTokens && query.totals.cached == 900000,
        "Distinct Claude requests with identical timestamps and usage are both retained");

      File.AppendAllText(fork, Reply(time.AddSeconds(20), "fork", "msg_replayed", "entry-d", 30, null));
      rows = collector.ReadUsageFiles(paths);
      check(rows.Length == 2 && rows.All(row => row.total == requestTokens && row.time == time),
        "A later partial replay cannot replace a completed Claude response or move it to a new period");

      var five = new WindowQuota {
        id = "five_hour", windowMinutes = 300, usedPercent = 53,
        observedAt = J.Iso(measured), resetsAt = J.Iso(time.AddHours(2))
      };
      var week = new WindowQuota {
        id = "seven_day", windowMinutes = 10080, usedPercent = 97,
        observedAt = J.Iso(measured), resetsAt = J.Iso(time.AddDays(1))
      };
      var expected = 2 * requestTokens;
      check(UsageEngine.ForWindow("claude", five, rows, measured).usedTokens == expected &&
          UsageEngine.ForWindow("claude", week, rows, measured).usedTokens == expected &&
          DataCollector.CalculateWindow("claude", five, rows, measured).sampleTokens == expected &&
          DataCollector.CalculateWindow("claude", week, rows, measured).sampleTokens == expected &&
          five.usedPercent == 53 && week.usedPercent == 97,
        "Both quota windows and estimates use the deduplicated Claude index while official percentages remain independent");

      string rewritten = Path.Combine(root, "rewritten.jsonl");
      File.WriteAllText(rewritten, Reply(time, "same-file", "msg_before", "entry-x", 17));
      collector.ReadUsageFile(rewritten, "claude");
      File.WriteAllText(rewritten, Reply(time, "same-file", "msg_after_", "entry-y", 19));
      File.SetLastWriteTimeUtc(rewritten, DateTime.UtcNow.AddSeconds(10));
      check(collector.ReadUsageFile(rewritten, "claude").Single().output == 19,
        "Replacing a Claude log at the same length rebuilds its indexed requests");

      File.WriteAllText(rewritten, Reply(time, "same-file", "msg_replace", "entry-z", 21) +
        Reply(time.AddSeconds(1), "same-file", "msg_append", "entry-w", 22, null));
      var replaced = collector.ReadUsageFile(rewritten, "claude");
      check(replaced.Count == 2 && replaced.Sum(row => row.output) == 43,
        "A larger rewritten log cannot keep stale usage from its previous contents");

      var oldFormat = DataCollector.ClaudeRecord(J.Parse(Reply(time, "older", null, "legacy-entry", 17)));
      var copiedOldFormat = DataCollector.ClaudeRecord(J.Parse(Reply(time, "newer", null, "legacy-entry", 17)));
      var requestFormat = DataCollector.ClaudeRecord(J.Parse(Reply(time, "newer", null, "other-entry", 17, requestId: "legacy-entry")));
      check(oldFormat.key == copiedOldFormat.key && oldFormat.key != requestFormat.key &&
          DataCollector.ClaudeRecord(J.Parse(Reply(time, "unknown", null, null, 17))) == null,
        "Older Claude entries keep stable fallback identifiers without conflating requests or guessing missing IDs");

      var sonnet = DataCollector.ClaudeRecord(J.Parse(Reply(time, "sonnet", "msg_sonnet", "sonnet-entry", 17,
        model: "claude-sonnet-test")));
      var opusWeek = new WindowQuota {
        id = "seven_day_opus", windowMinutes = 10080, usedPercent = 50,
        observedAt = week.observedAt, resetsAt = week.resetsAt
      };
      var mixedModels = rows.Concat(new[] { sonnet }).ToArray();
      check(UsageEngine.ForWindow("claude", opusWeek, mixedModels, measured).usedTokens == expected &&
          DataCollector.CalculateWindow("claude", opusWeek, mixedModels, measured)?.sampleTokens == expected &&
          UsageEngine.ForWindow("claude", week, mixedModels, measured).usedTokens == expected + requestTokens,
        "Model-specific Claude windows use their own model's rows in both usage and estimates");

      five.localUsage = UsageEngine.ForWindow("claude", five, rows, measured);
      five.estimate = DataCollector.CalculateWindow("claude", five, rows, measured);
      var oldSnapshot = new Snapshot {
        accounts = new List<Account> { new Account { id = "fixture", provider = "claude", windows = new List<WindowQuota> { five } } },
        models = query.models,
        daily = query.daily
      };
      string cache = Path.Combine(root, "old-usage-cache.json");
      J.Write(cache, oldSnapshot);
      var upgraded = ImportExport.LoadLocalCache(cache);
      check(upgraded.accounts[0].Five.usedPercent == 53 && upgraded.accounts[0].Five.resetsAt == five.resetsAt &&
          upgraded.accounts[0].Five.localUsage == null && upgraded.accounts[0].Five.estimate == null &&
          upgraded.models.Count == 0 && upgraded.daily.Count == 0,
        "Upgrading invalidates old derived totals and estimates without clearing the official quota reading");
      oldSnapshot.usageRevision = Snapshot.CurrentUsageRevision;
      J.Write(cache, oldSnapshot);
      var current = ImportExport.LoadLocalCache(cache);
      check(current.accounts[0].Five.localUsage.usedTokens == expected && current.models.Count == 1,
        "A corrected local snapshot keeps its totals on subsequent launches");

      var expired = new WindowQuota { usedPercent = 97, resetsAt = J.Iso(DateTime.UtcNow.AddMinutes(-1)), localUsage = five.localUsage };
      check(!expired.DisplayPercent.HasValue && expired.TokenText == "等待更新",
        "A reset quota window does not display the previous cycle's percentage or token count");
    }
  }
}
