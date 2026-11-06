using System;
using CapstoneGame.Online;
using NUnit.Framework;
using UnityEngine;

/// <summary>Edit Mode tests for the on-phone top 10 (<see cref="LocalScoreBoard"/>).</summary>
public class LocalScoreBoardTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private LocalScoreBoard board;

    [SetUp]
    public void SetUp() => board = new LocalScoreBoard(new MemoryLocalScoreStore(), () => Now);

    [Test]
    public void KeepsBestGamesFirst()
    {
        board.Record(100, 2, guest: true);
        board.Record(900, 6, guest: false);
        board.Record(450, 4, guest: true);

        Assert.AreEqual(new[] { 900, 450, 100 }, Scores());
        Assert.IsFalse(board.Top[0].guest);
        Assert.IsTrue(board.Top[1].guest);
        Assert.AreEqual(6, board.Top[0].wave);
        Assert.AreEqual("2026-11-06T12:00:00Z", board.Top[0].played_at);
    }

    [Test]
    public void RecordReturnsThePlace()
    {
        Assert.AreEqual(1, board.Record(100, 1, true));
        Assert.AreEqual(1, board.Record(200, 1, true));
        Assert.AreEqual(3, board.Record(50, 1, true));
        Assert.AreEqual(3, board.Record(100, 1, true), "A tie ranks below the earlier game with the same score.");
    }

    [Test]
    public void OnlyTheTop10AreKept()
    {
        for (int i = 1; i <= 12; i++) board.Record(i * 10, 1, true);

        Assert.AreEqual(LocalScoreBoard.MaxEntries, board.Top.Count);
        Assert.AreEqual(120, board.Top[0].score);
        Assert.AreEqual(30, board.Top[9].score);
        Assert.AreEqual(0, board.Record(5, 1, true), "A score below the top 10 doesn't make the list.");
        Assert.AreEqual(10, board.Top.Count);
    }

    [Test]
    public void NegativeScoresAndWavesAreClamped()
    {
        board.Record(-5, 0, true);
        Assert.AreEqual(0, board.Top[0].score);
        Assert.AreEqual(1, board.Top[0].wave);
    }

    [Test]
    public void PlayerPrefsBoardRoundTrips()
    {
        string backup = PlayerPrefs.GetString(PlayerPrefsLocalScoreStore.Key, null);
        try
        {
            var store = new PlayerPrefsLocalScoreStore();
            store.Save(null);
            new LocalScoreBoard(store, () => Now).Record(777, 3, guest: true);

            var reloaded = new LocalScoreBoard(new PlayerPrefsLocalScoreStore());
            Assert.AreEqual(777, reloaded.Top[0].score);
            Assert.IsTrue(reloaded.Top[0].guest);

            reloaded.Clear();
            Assert.AreEqual(0, reloaded.Top.Count);
        }
        finally
        {
            if (string.IsNullOrEmpty(backup)) PlayerPrefs.DeleteKey(PlayerPrefsLocalScoreStore.Key);
            else PlayerPrefs.SetString(PlayerPrefsLocalScoreStore.Key, backup);
        }
    }

    private int[] Scores()
    {
        var scores = new int[board.Top.Count];
        for (int i = 0; i < scores.Length; i++) scores[i] = board.Top[i].score;
        return scores;
    }
}
