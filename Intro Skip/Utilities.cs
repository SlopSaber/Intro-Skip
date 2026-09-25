using IPA.Utilities;
using System.IO;
using System.Linq;
using UnityEngine;

namespace IntroSkip
{
    internal static class Utilities
    {
        public static void MigrateConfig(ref Config config)
        {
            FileInfo oldFile = new FileInfo(Path.Combine(UnityGame.UserDataPath, "IntroSkip.ini"));
            if (oldFile.Exists)
            {
                string[] fileLines = File.ReadAllLines(oldFile.FullName);
                string? oldIntroLine = fileLines.FirstOrDefault(f => f.StartsWith("allowIntroSkip"));
                string? oldOutroLine = fileLines.FirstOrDefault(f => f.StartsWith("allowOutroSkip"));

                if (oldIntroLine != null)
                    config.AllowIntroSkip = oldIntroLine == "allowIntroSkip = True";

                if (oldOutroLine != null)
                    config.AllowOutroSkip = oldOutroLine == "allowOutroSkip = True";

                oldFile.Delete();
            }
        }
    }
}
