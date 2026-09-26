using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace BluePrinceHeadTracking.Tests.Differential
{
    /// <summary>
    /// Comparison 1: the published build's own PluginConfig (oracle/PluginConfig.cs, the file at
    /// 6c5513c byte for byte) against the frozen reader in src/BluePrinceHeadTracking/Legacy, on
    /// every input. A difference here is something players would see change that the conversion
    /// did not cause, and each one is listed with the commit that made it. There are none.
    /// </summary>
    public class ComparisonOneTests
    {
        [Fact]
        public void TheFrozenReaderReadsEveryInputAsThePublishedBuildDid()
        {
            List<DifferentialInput> inputs = Inputs.All().ToList();
            Assert.True(inputs.Count > 500, "the corpus gave only " + inputs.Count + " inputs");
            var failures = new ConcurrentBag<string>();
            var throwing = new ConcurrentBag<string>();
            Parallel.ForEach(inputs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, input =>
            {
                LegacyOutcome oracle = Oracle.Run(input);
                LegacyOutcome frozen = FrozenReader.Run(input);
                if (oracle.Error != null) throwing.Add(input.Name);
                if (oracle.Describe() != frozen.Describe())
                {
                    failures.Add(input.Name + ":\n" + Diff(oracle.Describe(), frozen.Describe()));
                }
                else if (oracle.Config != null && !LegacyStartup.Of(oracle.Config).SequenceEqual(LegacyStartup.Of(frozen.Config!)))
                {
                    failures.Add(input.Name + ": startup differs");
                }
            });
            Assert.True(failures.IsEmpty, string.Join("\n", failures.OrderBy(f => f, StringComparer.Ordinal).Take(20)));

            // BepInEx refuses a section header with a space inside its brackets, in the constructor
            // BasePlugin runs, so the published build never loaded on such a file, and nothing after
            // it does either: the plugin's own code never runs. These are the only such inputs.
            Assert.Equal(RefusedByBepInEx(), throwing.OrderBy(n => n, StringComparer.Ordinal));
        }

        internal static IEnumerable<string> RefusedByBepInEx()
        {
            return new[] { "Collision", "General", "Hotkeys", "Network", "Position", "Smoothing" }
                .Select(s => "corpus [" + s + "]: header with spaces")
                .OrderBy(n => n, StringComparer.Ordinal);
        }

        [Fact]
        public void EveryRecordedFileHoldsTheBytesItsProvenanceNames()
        {
            string root = Inputs.RepoRoot();
            string provenance = Path.Combine(root, "tests", "config_differential", "provenance.tsv");
            int checkedFiles = 0;
            foreach (string line in File.ReadAllLines(provenance))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] fields = line.Split('\t');
                Assert.Equal(3, fields.Length);
                string path = fields[1];
                if (path.Contains(':') || path.Contains(' ')) continue;
                string full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(full), path + " is missing");
                Assert.Equal(fields[2], Sha256(File.ReadAllBytes(full)));
                checkedFiles++;
            }
            Assert.True(checkedFiles >= 5, "provenance.tsv names only " + checkedFiles + " repo files");
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var text = new StringBuilder();
                foreach (byte b in sha.ComputeHash(bytes)) text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }

        private static string Diff(string expected, string actual)
        {
            string[] e = expected.Split('\n');
            string[] a = actual.Split('\n');
            var lines = new List<string>();
            for (int i = 0; i < e.Length && i < a.Length; i++)
            {
                if (e[i] != a[i]) lines.Add("  oracle " + e[i] + " | frozen " + a[i]);
            }
            return string.Join("\n", lines);
        }
    }
}
