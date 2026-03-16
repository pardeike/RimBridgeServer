using System;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class LogJournalTests
{
    [Fact]
    public void CollapsesRepeatedEntriesIntoASingleRow()
    {
        var journal = new LogJournal(collapseWindowMs: 5000);

        journal.Record("warning", "same message", "stack", "unity", DateTimeOffset.UtcNow);
        journal.Record("warning", "same message", "stack", "unity", DateTimeOffset.UtcNow.AddMilliseconds(100));

        var entries = journal.GetEntries();

        Assert.Single(entries);
        Assert.Equal(2, entries[0].RepeatCount);
    }

    [Fact]
    public void FiltersEntriesByLevelAndSequence()
    {
        var journal = new LogJournal();
        var info = journal.Record("info", "info message");
        var warning = journal.Record("warning", "warning message");
        journal.Record("error", "error message");

        var filtered = journal.GetEntries(limit: 10, minimumLevel: "warning", afterSequence: info.Sequence);

        Assert.Equal(2, filtered.Count);
        Assert.DoesNotContain(filtered, entry => entry.Sequence <= info.Sequence);
        Assert.Equal("warning", warning.Level);
    }
}
