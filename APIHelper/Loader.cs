using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CustomSpineLoader.APIHelper;

public class LoaderResult<T>
{
    public T Config { get; set; }
    public string FolderPath { get; set; }
    public string FolderName { get; set; }
}

public class Loader<T>
{
    public string FolderName { get; set; }

    public Loader(string folderName)
    {
        FolderName = folderName;
        var root = RootPath;
        if (!Directory.Exists(root))
            Directory.CreateDirectory(root);
    }

    public string RootPath => Path.Combine(Plugin.PluginPath, FolderName);

    // Scans each subfolder under RootPath for a single "config.json" and deserializes it to T.
    public List<LoaderResult<T>> LoadAll()
    {
        var results = new List<LoaderResult<T>>();
        var folders = Directory.GetDirectories(RootPath);
        Plugin.Log.LogInfo("Found " + folders.Length + " entries to load from " + FolderName + ".");

        foreach (var folder in folders)
        {
            var folderName = Path.GetFileName(folder);
            var configFiles = Directory.GetFiles(folder, "config.json", SearchOption.TopDirectoryOnly);
            if (configFiles.Length <= 0)
            {
                Plugin.Log.LogInfo("No config.json found for: " + folderName);
                continue;
            }

            try
            {
                var json = File.ReadAllText(configFiles[0]);
                var obj = JsonConvert.DeserializeObject<T>(json);
                if (obj != null)
                {
                    results.Add(new LoaderResult<T>
                    {
                        Config = obj,
                        FolderPath = folder,
                        FolderName = folderName
                    });
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to parse config.json for: " + folderName);
                Plugin.Log.LogError(e);
            }
        }

        return results;
    }
}
