//

using System;
using System.Collections.Generic;
using AutoHitCounter.Enums;
using AutoHitCounter.Interfaces;
using AutoHitCounter.Models;
using AutoHitCounter.Services;
using AutoHitCounter.ViewModels;
using NSubstitute;
using Xunit;

namespace AutoHitCounter.Tests.Services;

public class RunStateServiceTests
{
    private readonly IProfileService _profileService = Substitute.For<IProfileService>();
    private readonly RunStateService _sut;

    public RunStateServiceTests()
    {
        _sut = new RunStateService(_profileService);
    }

    private static SplitViewModel Child(string name = "Split", int hits = 0) =>
        new() { Name = name, Type = SplitType.Child, NumOfHits = hits };

    private static SplitViewModel Parent(string name = "Group") =>
        new() { Name = name, Type = SplitType.Parent };

    #region Capture

    [Fact]
    public void Capture_RecordsHitCountsForChildSplitsOnly()
    {
        var splits = new List<SplitViewModel> { Child(hits: 3), Parent(), Child(hits: 7) };

        var snapshot = _sut.Capture(splits, splits[0], false, TimeSpan.Zero);

        Assert.Equal(new[] { 3, 7 }, snapshot.HitCounts);
    }

    [Fact]
    public void Capture_CurrentSplitIndex_IsIndexInFullList()
    {
        var splits = new List<SplitViewModel> { Parent(), Child(), Child("Current") };

        var snapshot = _sut.Capture(splits, splits[2], false, TimeSpan.Zero);

        Assert.Equal(2, snapshot.CurrentSplitIndex);
    }

    [Fact]
    public void Capture_NullCurrentSplit_SetsIndexToMinusOne()
    {
        var splits = new List<SplitViewModel> { Child() };

        var snapshot = _sut.Capture(splits, null, false, TimeSpan.Zero);

        Assert.Equal(-1, snapshot.CurrentSplitIndex);
    }

    [Fact]
    public void Capture_RecordsIsRunComplete()
    {
        var splits = new List<SplitViewModel> { Child() };

        var snapshot = _sut.Capture(splits, splits[0], true, TimeSpan.Zero);

        Assert.True(snapshot.IsRunComplete);
    }

    [Fact]
    public void Capture_RecordsInGameTime()
    {
        var splits = new List<SplitViewModel> { Child() };
        var igt = TimeSpan.FromMinutes(12.5);

        var snapshot = _sut.Capture(splits, splits[0], false, igt);

        Assert.Equal(igt, snapshot.InGameTime);
    }

    [Fact]
    public void Capture_EmptySplitList_ReturnsEmptyHitCounts()
    {
        var snapshot = _sut.Capture(new List<SplitViewModel>(), null, false, TimeSpan.Zero);

        Assert.Empty(snapshot.HitCounts);
    }

    #endregion

    #region RestoreSnapshot

    [Fact]
    public void RestoreSnapshot_WritesHitCountsToChildSplits()
    {
        var a = Child();
        var b = Child();
        var splits = new List<SplitViewModel> { a, b };
        var snapshot = new RunSnapshot(0, new[] { 3, 7 }, null, false, TimeSpan.Zero);

        _sut.RestoreSnapshot(splits, snapshot);

        Assert.Equal(3, a.NumOfHits);
        Assert.Equal(7, b.NumOfHits);
    }

    [Fact]
    public void RestoreSnapshot_SkipsParentsWhenRestoringHits()
    {
        var child = Child();
        var splits = new List<SplitViewModel> { Parent(), child };
        var snapshot = new RunSnapshot(1, new[] { 5 }, null, false, TimeSpan.Zero);

        _sut.RestoreSnapshot(splits, snapshot);

        Assert.Equal(5, child.NumOfHits);
    }

    [Fact]
    public void RestoreSnapshot_ReturnsSplitAtCurrentSplitIndex()
    {
        var first = Child("First");
        var second = Child("Second");
        var splits = new List<SplitViewModel> { first, second };
        var snapshot = new RunSnapshot(1, new[] { 0, 0 }, null, false, TimeSpan.Zero);

        var result = _sut.RestoreSnapshot(splits, snapshot);

        Assert.Equal(second, result);
    }

    [Fact]
    public void RestoreSnapshot_IndexMinusOne_FallsBackToFirstChild()
    {
        var child = Child();
        var splits = new List<SplitViewModel> { child };
        var snapshot = new RunSnapshot(-1, new[] { 0 }, null, false, TimeSpan.Zero);

        var result = _sut.RestoreSnapshot(splits, snapshot);

        Assert.Equal(child, result);
    }

    [Fact]
    public void RestoreSnapshot_IndexOutOfRange_FallsBackToFirstChild()
    {
        var child = Child();
        var splits = new List<SplitViewModel> { child };
        var snapshot = new RunSnapshot(99, new[] { 0 }, null, false, TimeSpan.Zero);

        var result = _sut.RestoreSnapshot(splits, snapshot);

        Assert.Equal(child, result);
    }

    [Fact]
    public void RestoreSnapshot_MoreHitsThanSplits_DoesNotThrow()
    {
        var child = Child();
        var splits = new List<SplitViewModel> { child };
        var snapshot = new RunSnapshot(0, new[] { 1, 2, 3 }, null, false, TimeSpan.Zero);

        _sut.RestoreSnapshot(splits, snapshot);

        Assert.Equal(1, child.NumOfHits);
    }

    [Fact]
    public void RestoreSnapshot_FewerHitsThanSplits_LeavesRemainingAtZero()
    {
        var a = Child();
        var b = Child();
        var splits = new List<SplitViewModel> { a, b };
        var snapshot = new RunSnapshot(0, new[] { 4 }, null, false, TimeSpan.Zero);

        _sut.RestoreSnapshot(splits, snapshot);

        Assert.Equal(4, a.NumOfHits);
        Assert.Equal(0, b.NumOfHits);
    }

    #endregion

    #region Snapshot Store

    [Fact]
    public void TryGet_AfterSave_ReturnsSnapshot()
    {
        var snapshot = new RunSnapshot(0, new[] { 1 }, null, false, TimeSpan.Zero);

        _sut.Save("DS3", "Profile1", snapshot);

        Assert.True(_sut.TryGet("DS3", "Profile1", out var result));
        Assert.Equal(snapshot, result);
    }

    [Fact]
    public void TryGet_WhenNotSaved_ReturnsFalse()
    {
        Assert.False(_sut.TryGet("DS3", "Profile1", out _));
    }

    [Fact]
    public void Invalidate_RemovesSnapshot()
    {
        var snapshot = new RunSnapshot(0, new[] { 1 }, null, false, TimeSpan.Zero);
        _sut.Save("DS3", "Profile1", snapshot);

        _sut.Invalidate("DS3", "Profile1");

        Assert.False(_sut.TryGet("DS3", "Profile1", out _));
    }

    [Fact]
    public void Invalidate_WithNullGame_DoesNotThrow()
    {
        _sut.Invalidate(null, "Profile1");
    }

    [Fact]
    public void Invalidate_WithNullProfile_DoesNotThrow()
    {
        _sut.Invalidate("DS3", null);
    }

    [Fact]
    public void InvalidateStale_RemovesEntriesNotInValidSet()
    {
        var snapshot = new RunSnapshot(0, Array.Empty<int>(), null, false, TimeSpan.Zero);
        _sut.Save("DS3", "Keep", snapshot);
        _sut.Save("DS3", "Stale", snapshot);

        _sut.InvalidateStale("DS3", new[] { "Keep" });

        Assert.True(_sut.TryGet("DS3", "Keep", out _));
        Assert.False(_sut.TryGet("DS3", "Stale", out _));
    }

    [Fact]
    public void InvalidateStale_DoesNotAffectOtherGames()
    {
        var snapshot = new RunSnapshot(0, Array.Empty<int>(), null, false, TimeSpan.Zero);
        _sut.Save("DS3", "Profile1", snapshot);
        _sut.Save("ER", "Profile1", snapshot);

        _sut.InvalidateStale("DS3", Array.Empty<string>());

        Assert.True(_sut.TryGet("ER", "Profile1", out _));
    }

    [Fact]
    public void RenameGame_RekeysAllEntriesForOldName()
    {
        var snapshot = new RunSnapshot(0, Array.Empty<int>(), null, false, TimeSpan.Zero);
        _sut.Save("OldGame", "Profile1", snapshot);
        _sut.Save("OldGame", "Profile2", snapshot);

        _sut.RenameGame("OldGame", "NewGame");

        Assert.False(_sut.TryGet("OldGame", "Profile1", out _));
        Assert.False(_sut.TryGet("OldGame", "Profile2", out _));
        Assert.True(_sut.TryGet("NewGame", "Profile1", out _));
        Assert.True(_sut.TryGet("NewGame", "Profile2", out _));
    }

    [Fact]
    public void RenameGame_DoesNotAffectOtherGames()
    {
        var snapshot = new RunSnapshot(0, Array.Empty<int>(), null, false, TimeSpan.Zero);
        _sut.Save("OldGame", "Profile1", snapshot);
        _sut.Save("OtherGame", "Profile1", snapshot);

        _sut.RenameGame("OldGame", "NewGame");

        Assert.True(_sut.TryGet("OtherGame", "Profile1", out _));
    }

    #endregion

    #region SaveRunState

    [Fact]
    public void SaveRunState_WithNullProfile_DoesNotThrow()
    {
        _sut.SaveRunState(null, new List<SplitViewModel>(), null, false, TimeSpan.Zero);
    }

    [Fact]
    public void SaveRunState_BuildsRunStateWithCorrectHits()
    {
        var profile = new Profile();
        var splits = new List<SplitViewModel> { Child(hits: 3), Child(hits: 7) };

        _sut.SaveRunState(profile, splits, splits[0], false, TimeSpan.Zero);

        Assert.Equal(new[] { 3, 7 }, profile.SavedRun.HitCounts);
    }

    [Fact]
    public void SaveRunState_BuildsRunStateWithCorrectIndex()
    {
        var profile = new Profile();
        var splits = new List<SplitViewModel> { Child(), Child("Second") };

        _sut.SaveRunState(profile, splits, splits[1], false, TimeSpan.Zero);

        Assert.Equal(1, profile.SavedRun.CurrentSplitIndex);
    }

    [Fact]
    public void SaveRunState_BuildsRunStateWithCorrectIgt()
    {
        var profile = new Profile();
        var splits = new List<SplitViewModel> { Child() };
        var igt = TimeSpan.FromMinutes(5);

        _sut.SaveRunState(profile, splits, splits[0], false, igt);

        Assert.Equal((long)igt.TotalMilliseconds, profile.SavedRun.IgtMilliseconds);
    }

    [Fact]
    public void SaveRunState_BuildsRunStateWithIsRunComplete()
    {
        var profile = new Profile();
        var splits = new List<SplitViewModel> { Child() };

        _sut.SaveRunState(profile, splits, splits[0], true, TimeSpan.Zero);

        Assert.True(profile.SavedRun.IsRunComplete);
    }

    #endregion

    #region FlushRunState

    [Fact]
    public void FlushRunState_WhenSavedRunIsSet_CallsSaveProfile()
    {
        var profile = new Profile { SavedRun = new RunState() };

        _sut.FlushRunState(profile);

        _profileService.Received(1).SaveProfile(profile);
    }

    [Fact]
    public void FlushRunState_WhenSavedRunIsNull_DoesNotCallSaveProfile()
    {
        var profile = new Profile { SavedRun = null };

        _sut.FlushRunState(profile);

        _profileService.DidNotReceive().SaveProfile(Arg.Any<Profile>());
    }

    [Fact]
    public void FlushRunState_WithNullProfile_DoesNotThrow()
    {
        _sut.FlushRunState(null);
    }

    #endregion

    #region RestoreFromSavedRun

    [Fact]
    public void RestoreFromSavedRun_WritesHitsAndReturnsCorrectSplit()
    {
        var first = Child(hits: 0);
        var second = Child(hits: 0);
        var splits = new List<SplitViewModel> { first, second };
        var state = new RunState { CurrentSplitIndex = 1, HitCounts = new[] { 3, 7 }, IsRunComplete = false };

        var result = _sut.RestoreFromSavedRun(splits, state);

        Assert.Equal(3, first.NumOfHits);
        Assert.Equal(7, second.NumOfHits);
        Assert.Equal(second, result);
    }

    #endregion
}