using IPA.Utilities;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntroSkip
{
    internal static class Utilities
    {
        public static void MigrateConfig(ref Config config)
        {
            CultureInfo currentCulture = CultureInfo.CurrentCulture;
            CultureInfo? culture = currentCulture.GetType() == typeof(CultureInfo)
                && currentCulture.CompareInfo.GetType() == typeof(CompareInfo)
                ? CultureInfo.ReadOnly((CultureInfo)currentCulture.Clone())
                : null;
            MigrationResult result = Run(new MigrationRequest(
                Path.Combine(UnityGame.UserDataPath, "IntroSkip.ini"), culture, false));
            if (result.Path == null)
                return;

            bool? intro = result.Intro;
            bool? outro = result.Outro;
            if (result.Lines != null)
            {
                string? oldIntroLine = result.Lines.FirstOrDefault(f => f.StartsWith("allowIntroSkip"));
                string? oldOutroLine = result.Lines.FirstOrDefault(f => f.StartsWith("allowOutroSkip"));
                intro = oldIntroLine == null ? (bool?)null : oldIntroLine == "allowIntroSkip = True";
                outro = oldOutroLine == null ? (bool?)null : oldOutroLine == "allowOutroSkip = True";
            }

            if (intro.HasValue)
                config.AllowIntroSkip = intro.Value;
            if (outro.HasValue)
                config.AllowOutroSkip = outro.Value;

            Run(new MigrationRequest(result.Path, null, true));
        }

        private readonly struct MigrationResult
        {
            internal readonly string? Path;
            internal readonly string[]? Lines;
            internal readonly bool? Intro;
            internal readonly bool? Outro;

            internal MigrationResult(string path, string[]? lines, bool? intro, bool? outro)
            {
                Path = path;
                Lines = lines;
                Intro = intro;
                Outro = outro;
            }
        }

        private sealed class MigrationRequest
        {
            private readonly string path;
            private readonly CultureInfo? culture;
            private readonly bool delete;

            internal MigrationRequest(string path, CultureInfo? culture, bool delete)
            {
                this.path = path;
                this.culture = culture;
                this.delete = delete;
            }

            internal MigrationResult Execute()
            {
                var file = new FileInfo(path);
                if (delete)
                {
                    file.Delete();
                    return default;
                }
                if (!file.Exists)
                    return default;

                string[] lines = File.ReadAllLines(file.FullName);
                if (culture == null)
                    return new MigrationResult(file.FullName, lines, null, null);

                string? intro = lines.FirstOrDefault(f => f.StartsWith("allowIntroSkip", false, culture));
                string? outro = lines.FirstOrDefault(f => f.StartsWith("allowOutroSkip", false, culture));
                return new MigrationResult(file.FullName, null,
                    intro == null ? (bool?)null : intro == "allowIntroSkip = True",
                    outro == null ? (bool?)null : outro == "allowOutroSkip = True");
            }
        }

        private static MigrationResult Run(MigrationRequest request)
        {
            Task<MigrationResult> task;
            if (ExecutionContext.IsFlowSuppressed())
                task = Schedule(request);
            else
                using (ExecutionContext.SuppressFlow())
                    task = Schedule(request);

            try
            {
                if (!task.IsCompleted)
                    ((IAsyncResult)task).AsyncWaitHandle.WaitOne();
                return task.GetAwaiter().GetResult();
            }
            finally
            {
                if (task.IsCompleted)
                    task.Dispose();
            }
        }

        private static Task<MigrationResult> Schedule(MigrationRequest request)
        {
            return Task.Factory.StartNew(request.Execute, CancellationToken.None,
                TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }
    }
}
