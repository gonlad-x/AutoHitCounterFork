using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutoHitCounter.Models;
using AutoHitCounter.Services;
using AutoHitCounter.Utilities;
using Xunit;

namespace AutoHitCounter.Tests.Services;

[Collection("ProfileServiceFileTests")] 
public class ProfileServiceTests : IDisposable
{
    private static readonly string UserProfilesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoHitCounter", "Profiles.json");


    private static readonly string DefaultProfilesPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "DefaultProfiles.json");

    private readonly string _userBackupPath;
    private readonly bool _hadExistingUserFile;

    private readonly string _defaultBackupPath;
    private readonly bool _hadExistingDefaultFile;

    public ProfileServiceTests()
    {
        var dir = Path.GetDirectoryName(UserProfilesPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _userBackupPath = UserProfilesPath + ".testbackup";
        _hadExistingUserFile = File.Exists(UserProfilesPath);
        if (_hadExistingUserFile)
            MoveOverwrite(UserProfilesPath, _userBackupPath);

        _defaultBackupPath = DefaultProfilesPath + ".testbackup";
        _hadExistingDefaultFile = File.Exists(DefaultProfilesPath);
        if (_hadExistingDefaultFile)
            MoveOverwrite(DefaultProfilesPath, _defaultBackupPath);

        
        SettingsManager.Default.DeletedDefaultProfiles = string.Empty;
        SettingsManager.Default.Save();
    }

    public void Dispose()
    {
        if (_hadExistingUserFile)
            MoveOverwrite(_userBackupPath, UserProfilesPath);
        else
        {
            if (File.Exists(UserProfilesPath))
                File.Delete(UserProfilesPath);
            if (File.Exists(_userBackupPath))
                File.Delete(_userBackupPath);
        }

        if (_hadExistingDefaultFile)
            MoveOverwrite(_defaultBackupPath, DefaultProfilesPath);
        else
        {
            if (File.Exists(DefaultProfilesPath))
                File.Delete(DefaultProfilesPath);
            if (File.Exists(_defaultBackupPath))
                File.Delete(_defaultBackupPath);
        }

        SettingsManager.Default.DeletedDefaultProfiles = string.Empty;
        SettingsManager.Default.Save();
    }
    
    private static void MoveOverwrite(string sourcePath, string destPath)
    {
        if (File.Exists(destPath))
            File.Delete(destPath);
        File.Move(sourcePath, destPath);
    }

    private static void WriteDefaults(Dictionary<string, List<Profile>> defaults) =>
        File.WriteAllText(DefaultProfilesPath, JsonSerializer.Serialize(defaults));

    private static void WriteUserProfiles(Dictionary<string, List<Profile>> profiles) =>
        File.WriteAllText(UserProfilesPath, JsonSerializer.Serialize(profiles));

    private static ProfileService CreateSut() => new();

    private static Profile MakeProfile(string name, string game) =>
        new() { Name = name, GameName = game, Splits = new List<SplitEntry>() };

    #region GetProfiles

    [Fact]
    public void GetProfiles_UnknownGame_ReturnsEmptyList()
    {
        var sut = CreateSut();

        var result = sut.GetProfiles("NoSuchGame");

        Assert.Empty(result);
    }

    [Fact]
    public void GetProfiles_MergesDefaultsAndUserProfiles()
    {
        WriteDefaults(new()
        {
            ["Elden Ring"] = [MakeProfile("Default1", "Elden Ring")]
        });
        WriteUserProfiles(new()
        {
            ["Elden Ring"] = [MakeProfile("MyCustom", "Elden Ring")]
        });

        var sut = CreateSut();
        var result = sut.GetProfiles("Elden Ring");

        Assert.Contains(result, p => p.Name == "Default1");
        Assert.Contains(result, p => p.Name == "MyCustom");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetProfiles_UserProfileWithSameNameAsDefault_UserVersionWins()
    {
        WriteDefaults(new()
        {
            ["Elden Ring"] = [MakeProfile("Shared", "Elden Ring")]
        });
        var userVersion = MakeProfile("Shared", "Elden Ring");
        userVersion.AttemptCount = 42;
        WriteUserProfiles(new() { ["Elden Ring"] = [userVersion] });

        var sut = CreateSut();
        var result = sut.GetProfiles("Elden Ring");

        Assert.Single(result);
        Assert.Equal(42, result[0].AttemptCount);
    }

    [Fact]
    public void GetProfiles_TombstonedDefault_IsExcluded()
    {
        WriteDefaults(new()
        {
            ["Elden Ring"] = [MakeProfile("Default1", "Elden Ring"), MakeProfile("Default2", "Elden Ring")]
        });


        SettingsManager.Default.DeletedDefaultProfiles =
            JsonSerializer.Serialize(new HashSet<string> { "Elden Ring|Default1" });
        SettingsManager.Default.Save();

        var sut = CreateSut();
        var result = sut.GetProfiles("Elden Ring");

        Assert.DoesNotContain(result, p => p.Name == "Default1");
        Assert.Contains(result, p => p.Name == "Default2");
    }

    #endregion

    #region SaveProfile

    [Fact]
    public void SaveProfile_NewProfile_IsAdded()
    {
        var sut = CreateSut();
        var profile = MakeProfile("New", "CustomGame");

        sut.SaveProfile(profile);

        Assert.Contains(sut.GetProfiles("CustomGame"), p => p.Name == "New");
    }

    [Fact]
    public void SaveProfile_ExistingProfile_IsUpdatedInPlace()
    {
        var sut = CreateSut();
        var profile = MakeProfile("Existing", "CustomGame");
        sut.SaveProfile(profile);

        profile.AttemptCount = 7;
        sut.SaveProfile(profile);

        var result = sut.GetProfiles("CustomGame");
        Assert.Single(result);
        Assert.Equal(7, result[0].AttemptCount);
    }

    [Fact]
    public void SaveProfile_NullGameName_IsIgnored()
    {
        var sut = CreateSut();

        sut.SaveProfile(new Profile { Name = "Orphan", GameName = null });

        Assert.False(File.Exists(UserProfilesPath));
    }

    [Fact]
    public void SaveProfile_PersistsToUserProfilesFile()
    {
        var sut = CreateSut();
        sut.SaveProfile(MakeProfile("Persisted", "CustomGame"));

        Assert.True(File.Exists(UserProfilesPath));
        var onDisk = JsonSerializer.Deserialize<Dictionary<string, List<Profile>>>(
            File.ReadAllText(UserProfilesPath));

        Assert.Contains(onDisk["CustomGame"], p => p.Name == "Persisted");
    }

    [Fact]
    public void SaveProfile_ReSavingATombstonedDefault_ClearsItsTombstone()
    {
        WriteDefaults(new() { ["Elden Ring"] = [MakeProfile("Default1", "Elden Ring")] });
        var sut = CreateSut();
        sut.DeleteProfile("Elden Ring", "Default1");

        sut.SaveProfile(MakeProfile("Default1", "Elden Ring"));


        var reloaded = CreateSut();
        Assert.Contains(reloaded.GetProfiles("Elden Ring"), p => p.Name == "Default1");
    }

    #endregion

    #region DeleteProfile

    [Fact]
    public void DeleteProfile_RemovesFromList()
    {
        var sut = CreateSut();
        sut.SaveProfile(MakeProfile("A", "CustomGame"));
        sut.SaveProfile(MakeProfile("B", "CustomGame"));

        sut.DeleteProfile("CustomGame", "A");

        var result = sut.GetProfiles("CustomGame");
        Assert.DoesNotContain(result, p => p.Name == "A");
        Assert.Contains(result, p => p.Name == "B");
    }

    [Fact]
    public void DeleteProfile_UnknownGame_DoesNothing()
    {
        var sut = CreateSut();

        var ex = Record.Exception(() => sut.DeleteProfile("NoSuchGame", "Whatever"));

        Assert.Null(ex);
    }

    [Fact]
    public void DeleteProfile_LastUserProfile_NoDefaults_LeavesEmptyList()
    {
        var sut = CreateSut();
        sut.SaveProfile(MakeProfile("OnlyOne", "CustomGame"));

        sut.DeleteProfile("CustomGame", "OnlyOne");

        Assert.Empty(sut.GetProfiles("CustomGame"));
    }

    [Fact]
    public void DeleteProfile_OneOfSeveralDefaults_TombstonesItButDoesNotRepopulate()
    {
        WriteDefaults(new()
        {
            ["Elden Ring"] = [MakeProfile("Default1", "Elden Ring"), MakeProfile("Default2", "Elden Ring")]
        });
        var sut = CreateSut();

        sut.DeleteProfile("Elden Ring", "Default1");

        var result = sut.GetProfiles("Elden Ring");
        Assert.Single(result);
        Assert.Equal("Default2", result[0].Name);
    }

    [Fact]
    public void DeleteProfile_LastDefaultProfile_RepopulatesAllDefaults()
    {
        WriteDefaults(new()
        {
            ["Elden Ring"] = [MakeProfile("Default1", "Elden Ring"), MakeProfile("Default2", "Elden Ring")]
        });
        var sut = CreateSut();

        sut.DeleteProfile("Elden Ring", "Default1");
        sut.DeleteProfile("Elden Ring", "Default2"); 

        var result = sut.GetProfiles("Elden Ring");
        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Name == "Default1");
        Assert.Contains(result, p => p.Name == "Default2");
    }

    [Fact]
    public void DeleteProfile_RepopulatingDefaults_ClearsExistingTombstonesForThatGame()
    {
        WriteDefaults(new()
        {
            ["Elden Ring"] = [MakeProfile("Default1", "Elden Ring"), MakeProfile("Default2", "Elden Ring")]
        });
        var sut = CreateSut();
        sut.DeleteProfile("Elden Ring", "Default1"); 
        sut.DeleteProfile("Elden Ring", "Default2"); 


        var reloaded = CreateSut();
        var result = reloaded.GetProfiles("Elden Ring");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DeleteProfile_RepopulatedDefaults_AreIndependentClones()
    {
        WriteDefaults(new() { ["Elden Ring"] = [MakeProfile("Default1", "Elden Ring")] });
        var sut = CreateSut();
        sut.DeleteProfile("Elden Ring", "Default1"); 

        var result = sut.GetProfiles("Elden Ring");
        result[0].AttemptCount = 99;

        sut.DeleteProfile("Elden Ring", "Default1");
        var second = sut.GetProfiles("Elden Ring");
        Assert.Equal(0, second[0].AttemptCount);
    }

    [Fact]
    public void DeleteProfile_MixOfUserAndDefaultProfiles_LastOneDeleted_RepopulatesDefaultsOnly()
    {
        WriteDefaults(new() { ["Elden Ring"] = [MakeProfile("Default1", "Elden Ring")] });
        var sut = CreateSut();
        sut.SaveProfile(MakeProfile("Custom", "Elden Ring"));

        sut.DeleteProfile("Elden Ring", "Default1");
        sut.DeleteProfile("Elden Ring", "Custom");

        var result = sut.GetProfiles("Elden Ring");
        Assert.Single(result);
        Assert.Equal("Default1", result[0].Name);
    }

    [Fact]
    public void DeleteProfile_PersistsChangeToDisk()
    {
        var sut = CreateSut();
        sut.SaveProfile(MakeProfile("A", "CustomGame"));

        sut.DeleteProfile("CustomGame", "A");

        var onDisk = JsonSerializer.Deserialize<Dictionary<string, List<Profile>>>(
            File.ReadAllText(UserProfilesPath));
        Assert.DoesNotContain(onDisk["CustomGame"], p => p.Name == "A");
    }

    #endregion

    #region RenameGame

    [Fact]
    public void RenameGame_MovesAllProfilesToNewKey()
    {
        var sut = CreateSut();
        sut.SaveProfile(MakeProfile("A", "OldName"));
        sut.SaveProfile(MakeProfile("B", "OldName"));

        sut.RenameGame("OldName", "NewName");

        Assert.Empty(sut.GetProfiles("OldName"));
        var moved = sut.GetProfiles("NewName");
        Assert.Equal(2, moved.Count);
    }

    [Fact]
    public void RenameGame_UpdatesGameNameOnEachProfile()
    {
        var sut = CreateSut();
        sut.SaveProfile(MakeProfile("A", "OldName"));

        sut.RenameGame("OldName", "NewName");

        Assert.All(sut.GetProfiles("NewName"), p => Assert.Equal("NewName", p.GameName));
    }

    [Fact]
    public void RenameGame_UnknownGame_DoesNothing()
    {
        var sut = CreateSut();

        var ex = Record.Exception(() => sut.RenameGame("NoSuchGame", "NewName"));

        Assert.Null(ex);
        Assert.Empty(sut.GetProfiles("NewName"));
    }

    #endregion
}