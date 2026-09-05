using System.Collections.Generic;
using AutoHitCounter.Enums;
using AutoHitCounter.Interfaces;
using AutoHitCounter.Models;
using AutoHitCounter.ViewModels;
using NSubstitute;
using Xunit;

namespace AutoHitCounter.Tests.ViewModels;

public class ProfileEditorViewModelTests
{
    private readonly IProfileService _profileService = Substitute.For<IProfileService>();

    private ProfileEditorViewModel CreateVm()
    {
        _profileService.GetProfiles(Arg.Any<string>()).Returns([]);
        return new ProfileEditorViewModel(
            new Dictionary<uint, string>(),
            new Dictionary<uint, uint[]>(),
            _profileService,
            "TestGame",
            GameTitle.EldenRing,
            activeProfile: null);
    }

    private static SplitEntry Child(string name) =>
        new() { Name = name, Type = SplitType.Child };

    private static SplitEntry Parent(string name) =>
        new() { Name = name, Type = SplitType.Parent };

    #region MoveSplit - index adjustment

    [Fact]
    public void MoveSplit_ForwardMove_LandsAtCorrectPosition()
    {
        var vm = CreateVm();
        var a = Child("A");
        var b = Child("B");
        var c = Child("C");
        var d = Child("D");
        vm.Splits.Add(a);
        vm.Splits.Add(b);
        vm.Splits.Add(c);
        vm.Splits.Add(d);
        
        vm.MoveSplit(a, dropIndex: 2);

        Assert.Equal(new[] { b, a, c, d }, vm.Splits);
    }

    [Fact]
    public void MoveSplit_BackwardMove_LandsAtCorrectPosition()
    {
        var vm = CreateVm();
        var a = Child("A");
        var b = Child("B");
        var c = Child("C");
        var d = Child("D");
        vm.Splits.Add(a);
        vm.Splits.Add(b);
        vm.Splits.Add(c);
        vm.Splits.Add(d);
        
        vm.MoveSplit(c, dropIndex: 0);

        Assert.Equal(new[] { c, a, b, d }, vm.Splits);
    }

    [Fact]
    public void MoveSplit_MoveToLastPosition_LandsAtEnd()
    {
        var vm = CreateVm();
        var a = Child("A");
        var b = Child("B");
        var c = Child("C");
        vm.Splits.Add(a);
        vm.Splits.Add(b);
        vm.Splits.Add(c);
        
        vm.MoveSplit(a, dropIndex: 3);

        Assert.Equal(new[] { b, c, a }, vm.Splits);
    }

    #endregion

    #region MoveSplit - group assignment

    [Fact]
    public void MoveSplit_IntoGroup_AssignsGroupId()
    {
        var vm = CreateVm();
        var parent = Parent("Group");
        parent.GroupId = "group1";
        var a = Child("A");
        var b = Child("B");
        b.GroupId = "group1";
        vm.Splits.Add(a);
        vm.Splits.Add(parent);
        vm.Splits.Add(b);
        
        vm.MoveSplit(a, dropIndex: 2);

        Assert.Equal("group1", a.GroupId);
    }

    [Fact]
    public void MoveSplit_OutOfGroup_ClearsGroupId()
    {
        var vm = CreateVm();
        var parent = Parent("Group");
        parent.GroupId = "group1";
        var a = Child("A");
        a.GroupId = "group1";
        var b = Child("B");
        vm.Splits.Add(parent);
        vm.Splits.Add(a);
        vm.Splits.Add(b);
        
        vm.MoveSplit(a, dropIndex: 0);

        Assert.Null(a.GroupId);
    }

    [Fact]
    public void MoveSplit_BetweenGroups_AdoptsNewGroupId()
    {
        var vm = CreateVm();
        var parent1 = Parent("Group1");
        parent1.GroupId = "group1";
        var a = Child("A");
        a.GroupId = "group1";
        var parent2 = Parent("Group2");
        parent2.GroupId = "group2";
        var b = Child("B");
        b.GroupId = "group2";
        vm.Splits.Add(parent1);
        vm.Splits.Add(a);
        vm.Splits.Add(parent2);
        vm.Splits.Add(b);
        
        vm.MoveSplit(a, dropIndex: 3);

        Assert.Equal("group2", a.GroupId);
    }

    [Fact]
    public void MoveSplit_ParentEntry_GroupIdUntouched()
    {
        var vm = CreateVm();
        var parent1 = Parent("Group1");
        parent1.GroupId = "group1";
        var child = Child("A");
        var parent2 = Parent("Group2");
        parent2.GroupId = "group2";
        vm.Splits.Add(parent1);
        vm.Splits.Add(child);
        vm.Splits.Add(parent2);
        
        vm.MoveSplit(parent2, dropIndex: 0);

        Assert.Equal("group2", parent2.GroupId);
    }

    #endregion
}
