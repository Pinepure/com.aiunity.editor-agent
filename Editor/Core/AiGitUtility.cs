#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Text;

namespace AiUnity.EditorAgent
{
    internal sealed class AiGitCommandResult
    {
        public bool ok;
        public int exitCode;
        public string stdout;
        public string stderr;
        public string message;
    }

    internal static class AiGitUtility
    {
        [Serializable]
        internal sealed class AiGitChangedFilesResult
        {
            public string[] changedFiles;
            public string[] unstagedFiles;
            public string[] stagedFiles;
            public string[] untrackedFiles;
            public bool ok;
            public string error;
        }

        public static AiGitCommandResult Run(string arguments, int timeoutMs)
        {
            AiGitCommandResult result = new AiGitCommandResult();

            Process process = null;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = "git";
                start.Arguments = arguments ?? string.Empty;
                start.WorkingDirectory = AiEditorAgentPaths.ProjectRoot;
                start.UseShellExecute = false;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.CreateNoWindow = true;

                process = new Process();
                process.StartInfo = start;
                process.Start();

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(Math.Max(1000, timeoutMs)))
                {
                    try { process.Kill(); } catch { }
                    result.ok = false;
                    result.exitCode = -1;
                    result.stdout = stdout;
                    result.stderr = stderr;
                    result.message = "git command timed out";
                    return result;
                }

                result.exitCode = process.ExitCode;
                result.stdout = stdout ?? string.Empty;
                result.stderr = stderr ?? string.Empty;
                result.ok = process.ExitCode == 0;
                result.message = result.ok ? "ok" : (string.IsNullOrEmpty(result.stderr) ? "git failed" : result.stderr.Trim());
                return result;
            }
            catch (Exception e)
            {
                result.ok = false;
                result.exitCode = -1;
                result.stdout = string.Empty;
                result.stderr = string.Empty;
                result.message = e.Message;
                return result;
            }
            finally
            {
                if (process != null) process.Dispose();
            }
        }

        public static string BuildStatusSummaryJson(int timeoutMs)
        {
            AiGitCommandResult status = Run("status --short", timeoutMs);
            AiGitCommandResult branch = Run("rev-parse --abbrev-ref HEAD", timeoutMs);
            return "{"
                + "\"ok\":" + AiJson.Bool(status.ok) + ","
                + "\"branch\":" + AiJson.Quote(branch.ok ? branch.stdout.Trim() : string.Empty) + ","
                + "\"summary\":" + AiJson.Quote(NormalizeLines(status.stdout)) + ","
                + "\"error\":" + AiJson.Quote(status.ok ? string.Empty : status.message)
                + "}";
        }

        public static string BuildDiffSummaryJson(int timeoutMs)
        {
            AiGitChangedFilesResult changed = GetChangedFilesDetailed(timeoutMs);
            AiGitCommandResult staged = Run("diff --cached --name-only", timeoutMs);
            AiGitCommandResult untracked = Run("ls-files --others --exclude-standard", timeoutMs);
            AiGitCommandResult stat = Run("diff --stat --compact-summary", timeoutMs);
            AiGitCommandResult stagedStat = Run("diff --cached --stat --compact-summary", timeoutMs);

            string[] stagedFiles = SplitLines(staged.stdout);
            string[] untrackedFiles = SplitLines(untracked.stdout);
            return "{"
                + "\"ok\":" + AiJson.Bool(changed.ok && staged.ok && untracked.ok && stat.ok && stagedStat.ok) + ","
                + "\"changedFiles\":" + AiJson.StringArray(changed.changedFiles) + ","
                + "\"unstagedFiles\":" + AiJson.StringArray(changed.unstagedFiles) + ","
                + "\"stagedFiles\":" + AiJson.StringArray(stagedFiles) + ","
                + "\"untrackedFiles\":" + AiJson.StringArray(untrackedFiles) + ","
                + "\"stat\":" + AiJson.Quote(JoinNonEmptyBlocks(NormalizeLines(stat.stdout), NormalizeLines(stagedStat.stdout))) + ","
                + "\"error\":" + AiJson.Quote(changed.ok && staged.ok && untracked.ok && stat.ok && stagedStat.ok ? string.Empty : FirstError(changed.error, staged, untracked, stat, stagedStat))
                + "}";
        }

        public static string[] GetChangedFiles(int timeoutMs)
        {
            return GetChangedFilesDetailed(timeoutMs).changedFiles ?? new string[0];
        }

        public static AiGitChangedFilesResult GetChangedFilesDetailed(int timeoutMs)
        {
            AiGitCommandResult unstaged = Run("diff --name-only", timeoutMs);
            AiGitCommandResult staged = Run("diff --cached --name-only", timeoutMs);
            AiGitCommandResult untracked = Run("ls-files --others --exclude-standard", timeoutMs);

            string[] unstagedFiles = unstaged.ok ? SplitLines(unstaged.stdout) : new string[0];
            string[] stagedFiles = staged.ok ? SplitLines(staged.stdout) : new string[0];
            string[] untrackedFiles = untracked.ok ? SplitLines(untracked.stdout) : new string[0];
            string[] all = MergeUnique(unstagedFiles, stagedFiles, untrackedFiles);

            return new AiGitChangedFilesResult
            {
                changedFiles = all,
                unstagedFiles = unstagedFiles,
                stagedFiles = stagedFiles,
                untrackedFiles = untrackedFiles,
                ok = unstaged.ok && staged.ok && untracked.ok,
                error = unstaged.ok && staged.ok && untracked.ok ? string.Empty : FirstError(string.Empty, unstaged, staged, untracked)
            };
        }

        private static string FirstError(string preferred, params AiGitCommandResult[] results)
        {
            if (!string.IsNullOrEmpty(preferred)) return preferred;
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] != null && !results[i].ok) return results[i].message ?? string.Empty;
            }
            return string.Empty;
        }

        private static string NormalizeLines(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\r\n", "\n").Trim();
        }

        private static string[] SplitLines(string value)
        {
            string normalized = NormalizeLines(value);
            if (string.IsNullOrEmpty(normalized)) return new string[0];
            string[] raw = normalized.Split('\n');
            int count = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(raw[i])) count++;
            }

            string[] result = new string[count];
            int index = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                string line = raw[i] == null ? string.Empty : raw[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                result[index++] = line;
            }
            return result;
        }

        private static string[] MergeUnique(params string[][] groups)
        {
            System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            System.Collections.Generic.List<string> result = new System.Collections.Generic.List<string>();
            if (groups == null) return result.ToArray();
            for (int i = 0; i < groups.Length; i++)
            {
                string[] group = groups[i];
                if (group == null) continue;
                for (int j = 0; j < group.Length; j++)
                {
                    string value = group[j] == null ? string.Empty : group[j].Trim();
                    if (string.IsNullOrEmpty(value)) continue;
                    if (!seen.Add(value)) continue;
                    result.Add(value);
                }
            }
            return result.ToArray();
        }

        private static string JoinNonEmptyBlocks(string first, string second)
        {
            string a = string.IsNullOrEmpty(first) ? string.Empty : first.Trim();
            string b = string.IsNullOrEmpty(second) ? string.Empty : second.Trim();
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + "\n" + b;
        }
    }
}
#endif
