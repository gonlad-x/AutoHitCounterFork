using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AutoHitCounter.Interfaces;
using AutoHitCounter.Models;
using AutoHitCounter.Utilities;

namespace AutoHitCounter.Services;

public class ProfileService : IProfileService
{
    private readonly Dictionary<string, List<Profile>> _defaultProfiles;
    private readonly Dictionary<string, List<Profile>> _profiles;
    private readonly HashSet<string> _deletedDefaults;
    

    private static readonly string UserProfilesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoHitCounter",
        "Profiles.json");

    private static readonly string DefaultProfilesPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "DefaultProfiles.json");

    public ProfileService()
    {
        _defaultProfiles = LoadFromDisk(DefaultProfilesPath);
        _deletedDefaults = LoadDeletedDefaults();
        _profiles = LoadMergedProfiles();
    }

    public List<Profile> GetProfiles(string gameName)
    {
        return _profiles.TryGetValue(gameName, out var profiles)
            ? profiles
            : [];
    }

    public void SaveProfile(Profile profile)
    {
        if (profile?.GameName == null) return;
        if (!_profiles.ContainsKey(profile.GameName))
            _profiles[profile.GameName] = [];

        var list = _profiles[profile.GameName];
        var existing = list.FindIndex(p => p.Name == profile.Name);

        if (existing >= 0)
            list[existing] = profile;
        else
            list.Add(profile);

        if (_deletedDefaults.Remove(TombstoneKey(profile.GameName, profile.Name)))
            SaveDeletedDefaults(_deletedDefaults);

        TryWriteUserProfiles();
    }

    public void DeleteProfile(string gameName, string profileName)
    {
        if (!_profiles.TryGetValue(gameName, out var list)) return;
        list.RemoveAll(p => p.Name == profileName);
        // Checking if game has default profiles to repopulate it with them on next launch if no profiles are found for said game
        bool hasDefaults = _defaultProfiles.TryGetValue(gameName, out var defaults);
        bool isDefault = hasDefaults && defaults.Exists(p => p.Name == profileName);

        if (isDefault)
            _deletedDefaults.Add(TombstoneKey(gameName, profileName));

        if (list.Count == 0 && hasDefaults)
            {
                _deletedDefaults.RemoveWhere(k => k.StartsWith(gameName + "|", StringComparison.Ordinal));
                _profiles[gameName] = defaults.ConvertAll(CloneProfile);
            }
        SaveDeletedDefaults(_deletedDefaults);
        TryWriteUserProfiles();
    }

    public void RenameGame(string oldGameName, string newGameName)
    {
        if (!_profiles.TryGetValue(oldGameName, out var list)) return;

        _profiles.Remove(oldGameName);
        foreach (var profile in list)
            profile.GameName = newGameName;
        _profiles[newGameName] = list;

        TryWriteUserProfiles();
    }
    
    private static string TombstoneKey(string gameName, string profileName) => $"{gameName}|{profileName}";

    private static Profile CloneProfile(Profile source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<Profile>(json);
    }

    private Dictionary<string, List<Profile>> LoadMergedProfiles()
    {
        var user = LoadFromDisk(UserProfilesPath);

        var merged = new Dictionary<string, List<Profile>>();

        foreach (var kvp in _defaultProfiles)
        {
            var kept = kvp.Value.FindAll(p => !_deletedDefaults.Contains(TombstoneKey(kvp.Key, p.Name)));
            if (kept.Count > 0)
                merged[kvp.Key] = kept.ConvertAll(CloneProfile);
        }

        foreach (var kvp in user)
        {
            if (!merged.TryGetValue(kvp.Key, out var mergedList))
            {
                merged[kvp.Key] = new List<Profile>(kvp.Value);
                continue;
            }

            foreach (var userProfile in kvp.Value)
            {
                var existingIndex = mergedList.FindIndex(p => p.Name == userProfile.Name);

                if (existingIndex >= 0)
                    mergedList[existingIndex] = userProfile;
                else
                    mergedList.Add(userProfile);
            }
        }

        return merged;
    }

    private static Dictionary<string, List<Profile>> LoadFromDisk(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new Dictionary<string, List<Profile>>();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, List<Profile>>>(json)
                   ?? new Dictionary<string, List<Profile>>();
        }
        catch
        {
            return new Dictionary<string, List<Profile>>();
        }
    }

    private readonly object _profileWriteLock = new();

    private void TryWriteUserProfiles()
    {
        lock (_profileWriteLock)
        {
            var tempPath = UserProfilesPath + ".tmp";

            try
            {
                var dir = Path.GetDirectoryName(UserProfilesPath);

                if (string.IsNullOrEmpty(dir))
                    throw new InvalidOperationException("Invalid user profiles path.");

                Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(
                    _profiles,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(tempPath, json);

                if (File.Exists(UserProfilesPath))
                    File.Replace(tempPath, UserProfilesPath, UserProfilesPath + ".bak");
                else
                    File.Move(tempPath, UserProfilesPath);
            }
            catch (IOException ex)
            {
                Logger.Error(ex, $"Failed to write profiles to '{UserProfilesPath}'.");
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Error(ex, $"Access denied writing profiles to '{UserProfilesPath}'.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Unexpected error writing profiles to '{UserProfilesPath}'.");
            }
        }
    }

    private static HashSet<string> LoadDeletedDefaults()
    {
        var raw = SettingsManager.Default.DeletedDefaultProfiles;
        if (string.IsNullOrWhiteSpace(raw)) return [];

        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(raw) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void SaveDeletedDefaults(HashSet<string> deleted)
    {
        SettingsManager.Default.DeletedDefaultProfiles = JsonSerializer.Serialize(deleted);
        SettingsManager.Default.Save();
    }
}