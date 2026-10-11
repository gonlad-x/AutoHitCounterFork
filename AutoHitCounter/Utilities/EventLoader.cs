// 

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AutoHitCounter.Properties;

namespace AutoHitCounter.Utilities;

public static class EventLoader
{
    public static Dictionary<uint, string> GetEvents(string resourceName)
    {
        var dict = new Dictionary<uint, string>();
        string csvData = Resources.ResourceManager.GetString(resourceName);
        if (string.IsNullOrWhiteSpace(csvData)) return dict;

        var regex = new Regex(@"^""?([^""]*)""?\s*,\s*(\d+)$", RegexOptions.Compiled);
    
        using var reader = new StringReader(csvData);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
        
            var match = regex.Match(line.Trim());
            if (match.Success && uint.TryParse(match.Groups[2].Value, out uint id))
            {
                dict[id] = match.Groups[1].Value;
            }
        }
        return dict;
    }

    // A flag can map to more than one Entity ID -- e.g. a Gauntlet-mode instance of a
    // fight uses a different placed NPC than the main-mode encounter, but both should
    // start the same split's boss-timer. Multiple CSV rows sharing the same flag are
    // aggregated into one entry rather than the last row silently overwriting the rest.
    public static Dictionary<uint, uint[]> GetEntityIds(string resourceName)
    {
        var dict = new Dictionary<uint, List<uint>>();
        string csvData = Resources.ResourceManager.GetString(resourceName);
        if (string.IsNullOrWhiteSpace(csvData)) return new Dictionary<uint, uint[]>();

        var regex = new Regex(@"^(\d+)\s*,\s*(\d+)$", RegexOptions.Compiled);

        using var reader = new StringReader(csvData);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

            var match = regex.Match(line.Trim());
            if (match.Success
                && uint.TryParse(match.Groups[1].Value, out uint flag)
                && uint.TryParse(match.Groups[2].Value, out uint entityId))
            {
                if (!dict.TryGetValue(flag, out var entityIds))
                    dict[flag] = entityIds = new List<uint>();
                entityIds.Add(entityId);
            }
        }
        return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
    }
}