using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BluePrinceHeadTracking.Configuration;
using BluePrinceHeadTracking.Legacy;
using CameraUnlock.Core.Config;
using CameraUnlock.Core.Input;
using UnityEngine;
using Xunit;

namespace BluePrinceHeadTracking.Tests.Differential
{
    /// <summary>
    /// Comparison 2, the import against the migration, and what the import does with each value
    /// the published build ran on. Every input of comparison 1 is migrated into a new
    /// CameraUnlock.ini, from a writable and from a read-only legacy file, once over the built-in
    /// Defaults.ini and once over a Defaults.ini that differs on every row this game takes from it.
    /// </summary>
    public class ComparisonTwoTests
    {
        /// <summary>Every global row the table binds, each away from its built-in value.</summary>
        private const string OtherDefaults =
            "[CameraUnlock]\r\nConfigFormat=1\r\n\r\n" +
            "[Network]\r\nUdpPort=4343\r\n\r\n" +
            "[General]\r\nEnableOnStartup=false\r\nWorldSpaceYaw=false\r\nRotationEnabled=true\r\n\r\n" +
            "[Smoothing]\r\nLocalSmoothing=0.25\r\nRemoteSmoothing=0.35\r\n\r\n" +
            "[Position]\r\nPositionEnabled=false\r\nPositionLimitX=0.26\r\nPositionLimitY=0.16\r\nPositionLimitYDown=0.17\r\n" +
            "PositionLimitZ=0.36\r\nPositionLimitZBack=0.06\r\nCollisionEnabled=false\r\nCollisionReleaseSmoothing=0.5\r\n\r\n" +
            "[Hotkeys]\r\nToggleKey=F8\r\nCycleTrackingModeKey=F7\r\nYawModeKey=F6\r\n";

        // A .cfg can hold a number for a key, which BepInEx's enum parse accepts and Unity names no
        // key for. No hotkey list can hold it and no approved rule drops it, so the config owner
        // defers these imports: the player keeps what the published build ran on, nothing is
        // written, and the import runs again at the next start.
        // These are unresolved, not accepted: core's config-format.json has no rule for them yet
        // (N1 covers native virtual-key codes only). Once it records one, the map applies it and
        // this list is deleted. An input outside it that the codecs cannot hold still fails here.
        private static readonly string[] DeferredValues = { "value 010", "value -1", "value +1", "value space then 1", "value 1 then space", "value 2" };

        private static IEnumerable<string> Deferred()
        {
            return new[] { "[Hotkeys] ToggleKey", "[Hotkeys] CycleTrackingModeKey", "[Hotkeys] YawModeKey" }
                .SelectMany(key => DeferredValues.Select(v => "corpus " + key + ": " + v))
                .OrderBy(n => n, StringComparer.Ordinal);
        }

        private static readonly Lazy<string> MigratedDir = new Lazy<string>(() =>
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "migrated");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            return dir;
        });

        [Fact]
        public void TheMigrationHoldsWhatTheImportReadOverTheBuiltInDefaults()
        {
            Compare(null);
        }

        [Fact]
        public void TheMigrationHoldsWhatTheImportReadOverOtherDefaults()
        {
            Compare(OtherDefaults);
        }

        private static void Compare(string? defaultsIni)
        {
            List<DifferentialInput> inputs = Inputs.All().ToList();
            var failures = new ConcurrentBag<string>();
            var deferred = new ConcurrentBag<string>();
            var refused = new ConcurrentBag<string>();
            var created = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
            byte[] committed = File.ReadAllBytes(ConfigTests.Committed());
            var defaults = new BluePrinceConfig();
            BluePrinceConfig.Table().Apply(CanonicalIni.Parse(defaultsIni == null ? new byte[0] : Encoding.ASCII.GetBytes(defaultsIni)), defaults);
            Dictionary<string, string> defaultLines = Lines(MigrationOutcome.Describe(defaults));
            Parallel.ForEach(inputs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, input =>
            {
                ImportOutcome import = ImportOutcome.Run(input);
                foreach (bool readOnly in input.Bytes == null ? new[] { false } : new[] { false, true })
                {
                    string name = input.Name + (readOnly ? " (read-only)" : "");
                    MigrationOutcome migration = MigrationOutcome.Run(input, defaultsIni, readOnly);

                    if (import.Error != null || migration.Error != null)
                    {
                        if (import.Error != migration.Error) failures.Add(name + ": import " + import.Error + ", migration " + migration.Error);
                        if (!readOnly) refused.Add(input.Name);
                        continue;
                    }

                    string imported = FollowingDefaultsIni(MigrationOutcome.Describe(import.Config!), import.Result!, defaultLines);
                    string migrated = MigrationOutcome.Describe(migration.Config!);
                    if (input.Bytes == null)
                    {
                        if (migration.Status != ConfigLoadStatus.Created) failures.Add(name + ": " + migration.Status);
                        if (!migration.Created!.SequenceEqual(committed)) failures.Add(name + ": the created file is not config/CameraUnlock.ini");
                        // Every code default is the value the published build shipped, so without a
                        // file the new CameraUnlock.ini holds what it ran on, as long as Defaults.ini
                        // gives the built-in values.
                        if (defaultsIni == null && imported != migrated) failures.Add(name + ":\n" + Diff(imported, migrated));
                        continue;
                    }

                    if (migration.Status == ConfigLoadStatus.Deferred)
                    {
                        if (!readOnly) deferred.Add(input.Name);
                        if (!migration.Reason!.Contains("cannot be converted")) failures.Add(name + ": deferred: " + migration.Reason);
                    }
                    else if (migration.Status != ConfigLoadStatus.Migrated)
                    {
                        failures.Add(name + ": " + migration.Status + ": " + migration.Reason);
                        continue;
                    }
                    else
                    {
                        created[Sha256(migration.Created!)] = migration.Created!;
                        string text = Encoding.ASCII.GetString(migration.Created!);
                        foreach (ConceptDescriptor concept in import.Result!.FollowsDefaultsIni)
                        {
                            if (!text.Contains("\r\n" + concept.Key + "=default\r\n"))
                                failures.Add(name + ": " + concept.Key + " follows Defaults.ini and is not written default");
                        }
                    }
                    if (imported != migrated) failures.Add(name + ":\n" + Diff(imported, migrated));
                }
            });
            Assert.True(failures.IsEmpty, string.Join("\n", failures.OrderBy(f => f, StringComparer.Ordinal).Take(20)));
            // Handed to core's canonical config lint by tests/config_differential/lint-migrated.mjs,
            // which pixi run test runs next.
            foreach (KeyValuePair<string, byte[]> file in created)
            {
                File.WriteAllBytes(Path.Combine(MigratedDir.Value, file.Key + ".ini"), file.Value);
            }
            Assert.Equal(ComparisonOneTests.RefusedByBepInEx(), refused.OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(Deferred(), deferred.OrderBy(n => n, StringComparer.Ordinal));
        }

        /// <summary>
        /// What the migration gives: the import, with every row it leaves to Defaults.ini at the
        /// value <paramref name="defaultLines"/> holds for it.
        /// </summary>
        private static string FollowingDefaultsIni(string described, ImportResult result, Dictionary<string, string> defaultLines)
        {
            Dictionary<string, string> lines = Lines(described);
            foreach (ConceptDescriptor concept in result.FollowsDefaultsIni)
            {
                foreach (string name in new[] { concept.Key, "Position." + concept.Key })
                {
                    if (lines.ContainsKey(name)) lines[name] = defaultLines[name];
                }
            }
            var s = new StringBuilder();
            foreach (string name in Lines(described).Keys) s.Append(name).Append('=').Append(lines[name]).Append('\n');
            return s.ToString();
        }

        private static Dictionary<string, string> Lines(string described)
        {
            var lines = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in described.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = line.IndexOf('=');
                lines.Add(line.Substring(0, eq), line.Substring(eq + 1));
            }
            return lines;
        }

        /// <summary>
        /// The map proof: on every input, what the converted plugin runs on from the import is what
        /// the published build ran on, apart from exactly the values the approved changes drop.
        /// </summary>
        [Fact]
        public void TheImportKeepsEverySettingButTheApprovedDrops()
        {
            var failures = new ConcurrentBag<string>();
            Parallel.ForEach(Inputs.All().ToList(), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, input =>
            {
                LegacyOutcome oracle = Oracle.Run(input);
                ImportOutcome import = ImportOutcome.Run(input);
                if (oracle.Error != null)
                {
                    if (import.Error != oracle.Error) failures.Add(input.Name + ": " + import.Error);
                    return;
                }
                LegacyConfig old = oracle.Config!;
                ImportResult result = import.Result!;
                ImportStatus status = input.Bytes == null ? ImportStatus.Absent : ImportStatus.Imported;
                if (result.Status != status) failures.Add(input.Name + ": " + result.Status);

                SortedDictionary<string, string> before = LegacyStartup.Of(old);
                var expectedDrops = new List<string>();
                if (!old.ShowReticle) expectedDrops.Add("Reticle General ShowReticle false");
                Action<string, KeyCode, KeyCode> hotkey = (key, primary, letter) =>
                {
                    if (!IsModifier(primary)) return;
                    before[key] = LegacyStartup.Hotkey(KeyCode.None, letter);
                    expectedDrops.Add("ModifierKey Hotkeys " + key + " " + primary);
                };
                hotkey("ToggleKey", old.ToggleKey, KeyCode.Y);
                hotkey("CycleTrackingModeKey", old.CycleTrackingModeKey, KeyCode.G);
                hotkey("YawModeKey", old.YawModeKey, KeyCode.H);
                SortedDictionary<string, string> after = ConvertedStartup.Of(import.Config!);
                bool pointerDropped = result.Dropped.Any(d => d.Rule == DropRule.Reticle && d.Key == "ShowReticle");
                foreach (string key in before.Keys)
                {
                    if (key == "CrosshairFollowsAim" && pointerDropped) continue;
                    if (before[key] != after[key]) failures.Add(input.Name + ": " + key + " " + before[key] + " -> " + after[key]);
                }

                string[] drops = result.Dropped.Select(d => d.Rule + " " + d.Section + " " + d.Key + " " + d.Value).ToArray();
                if (!drops.SequenceEqual(expectedDrops)) failures.Add(input.Name + ": dropped " + string.Join("; ", drops));
                if (result.PoseShaping.Count != 0) failures.Add(input.Name + ": pose shaping " + result.PoseShaping.Count);

                // A setting the player never changed from the published build's default follows
                // Defaults.ini, the tracking mode as one unit.
                var shipped = new LegacyConfig();
                var expectedFollows = new List<string>();
                Action<string, bool> follows = (key, unchanged) => { if (unchanged) expectedFollows.Add(key); };
                follows("UdpPort", old.UdpPort == shipped.UdpPort);
                follows("EnableOnStartup", old.EnabledOnStartup == shipped.EnabledOnStartup);
                follows("WorldSpaceYaw", old.WorldSpaceYaw == shipped.WorldSpaceYaw);
                follows("RotationEnabled", old.PositionEnabled == shipped.PositionEnabled);
                follows("PositionEnabled", old.PositionEnabled == shipped.PositionEnabled);
                follows("LocalSmoothing", old.LocalSmoothing.Equals(shipped.LocalSmoothing));
                follows("RemoteSmoothing", old.RemoteSmoothing.Equals(shipped.RemoteSmoothing));
                follows("PositionLimitX", old.PositionLimitX.Equals(shipped.PositionLimitX));
                follows("PositionLimitY", old.PositionLimitY.Equals(shipped.PositionLimitY));
                follows("PositionLimitYDown", old.PositionLimitYDown.Equals(shipped.PositionLimitYDown));
                follows("PositionLimitZ", old.PositionLimitZ.Equals(shipped.PositionLimitZ));
                follows("PositionLimitZBack", old.PositionLimitZBack.Equals(shipped.PositionLimitZBack));
                follows("CollisionEnabled", old.CollisionEnabled == shipped.CollisionEnabled);
                follows("CollisionReleaseSmoothing", old.CollisionReleaseSmoothing.Equals(shipped.CollisionReleaseSmoothing));
                follows("ToggleKey", old.ToggleKey == shipped.ToggleKey);
                follows("CycleTrackingModeKey", old.CycleTrackingModeKey == shipped.CycleTrackingModeKey);
                follows("YawModeKey", old.YawModeKey == shipped.YawModeKey);
                string[] followed = result.FollowsDefaultsIni.Select(c => c.Key).ToArray();
                if (!followed.SequenceEqual(expectedFollows)) failures.Add(input.Name + ": follows Defaults.ini " + string.Join(", ", followed));
            });
            Assert.True(failures.IsEmpty, string.Join("\n", failures.OrderBy(f => f, StringComparer.Ordinal).Take(20)));
        }

        /// <summary>
        /// Fresh equals upgrade: the published build's first-run file migrates over the built-in
        /// Defaults.ini into the committed file byte for byte, dropping nothing.
        /// </summary>
        [Fact]
        public void TheFirstRunFileMigratesToTheCommittedFile()
        {
            byte[] committed = File.ReadAllBytes(ConfigTests.Committed());
            DifferentialInput input = Inputs.FirstRun();
            MigrationOutcome migration = MigrationOutcome.Run(input, null, false);

            Assert.Equal(ConfigLoadStatus.Migrated, migration.Status);
            Assert.Equal(Encoding.ASCII.GetString(committed), Encoding.ASCII.GetString(migration.Created!));
            Assert.Empty(ImportOutcome.Run(input).Result!.Dropped);
            Assert.DoesNotContain(migration.Log, l => l.Contains("not carried"));
        }

        /// <summary>Every KeyCode a .cfg can name converts to the key name that reads back as it.</summary>
        [Fact]
        public void EveryUnityKeyCodeConvertsToItsName()
        {
            string keys = File.ReadAllText(Path.Combine(ConfigTests.RepoRoot(), "cameraunlock-core", "data", "keys.json"));
            var codes = new List<int>();
            foreach (Match m in Regex.Matches(keys, "\"unity\":\\s*(\\d+)"))
            {
                codes.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            Assert.True(codes.Count > 300, "keys.json gave " + codes.Count + " Unity codes");
            foreach (int code in codes.Where(c => c != 0 && !IsModifier((KeyCode)c)))
            {
                var dropped = new List<DroppedValue>();
                string list = LegacyConfigImport.HotkeyList((KeyCode)code, KeyCode.Y, "ToggleKey", dropped);
                Assert.True(KeyBindings.TryParse(list, out KeyBinding[] bindings, out string? error), code + ": " + list + ": " + error);
                Assert.Equal(new KeyBinding(KeyModifiers.None, code), bindings[0]);
                Assert.Equal(new KeyBinding(KeyModifiers.Ctrl | KeyModifiers.Shift, (int)KeyCode.Y), bindings[1]);
                Assert.Empty(dropped);
            }
            var none = new List<DroppedValue>();
            Assert.Equal("Ctrl+Shift+G", LegacyConfigImport.HotkeyList(KeyCode.None, KeyCode.G, "CycleTrackingModeKey", none));
            Assert.Empty(none);
        }

        /// <summary>
        /// Normalisation N3: a Ctrl, Shift or Alt key on its own is left unbound, the drop is logged,
        /// and the action keeps its Ctrl+Shift chord.
        /// </summary>
        [Fact]
        public void AModifierKeyOnItsOwnImportsAsUnboundAndKeepsTheChord()
        {
            foreach (KeyCode modifier in Modifiers)
            {
                var dropped = new List<DroppedValue>();
                Assert.Equal("Ctrl+Shift+Y", LegacyConfigImport.HotkeyList(modifier, KeyCode.Y, "ToggleKey", dropped));
                DroppedValue drop = Assert.Single(dropped);
                Assert.Equal(DropRule.ModifierKey, drop.Rule);
                Assert.Equal("Hotkeys", drop.Section);
                Assert.Equal("ToggleKey", drop.Key);
                Assert.Equal(modifier.ToString(), drop.Value);
            }

            MigrationOutcome migration = MigrationOutcome.Run(
                new DifferentialInput("ToggleKey = RightShift", Edited("ToggleKey = End", "ToggleKey = RightShift")), null, false);
            Assert.Equal(ConfigLoadStatus.Migrated, migration.Status);
            Assert.Equal("Ctrl+Shift+Y", migration.Config!.ToggleKeyName);
            Assert.Contains("\r\nToggleKey=Ctrl+Shift+Y\r\n", Encoding.ASCII.GetString(migration.Created!));
            Assert.Contains(migration.Log, l => l.Contains("ToggleKey=RightShift, it is a Ctrl, Shift or Alt key"));
        }

        /// <summary>
        /// A setting the player never changed from the published build's default takes
        /// Defaults.ini's value and is written default, whatever Defaults.ini holds. A setting the
        /// player changed keeps its value. [Position] Enabled=true, the shipped default, leaves the
        /// whole tracking mode to Defaults.ini.
        /// </summary>
        [Fact]
        public void AnUntouchedSettingFollowsDefaultsIniAndAChangedOneStays()
        {
            MigrationOutcome untouched = MigrationOutcome.Run(Inputs.FirstRun(), OtherDefaults, false);
            Assert.Equal(ConfigLoadStatus.Migrated, untouched.Status);
            BluePrinceConfig c = untouched.Config!;
            Assert.Equal(4343, c.UdpPort);
            Assert.False(c.EnableOnStartup);
            Assert.False(c.WorldSpaceYaw);
            Assert.True(c.RotationEnabled);
            Assert.False(c.PositionEnabled);
            Assert.Equal(0.25f, c.LocalSmoothing);
            Assert.Equal(0.35f, c.RemoteSmoothing);
            Assert.Equal(0.26f, c.Position.LimitX);
            Assert.Equal(0.16f, c.Position.LimitY);
            Assert.Equal(0.17f, c.Position.LimitYDown);
            Assert.Equal(0.36f, c.Position.LimitZ);
            Assert.Equal(0.06f, c.Position.LimitZBack);
            Assert.False(c.CollisionEnabled);
            Assert.Equal(0.5f, c.CollisionReleaseSmoothing);
            Assert.Equal("F8", c.ToggleKeyName);
            Assert.Equal("F7", c.CycleTrackingModeKeyName);
            Assert.Equal("F6", c.YawModeKeyName);
            Assert.Equal(0.12f, c.CollisionMargin);
            Assert.Equal(Encoding.ASCII.GetString(File.ReadAllBytes(ConfigTests.Committed())),
                Encoding.ASCII.GetString(untouched.Created!));

            byte[] legacy = Edited("ToggleKey = End", "ToggleKey = F9", "WorldSpaceYaw = true", "WorldSpaceYaw = false",
                "\nEnabled = true", "\nEnabled = false");
            MigrationOutcome changed = MigrationOutcome.Run(new DifferentialInput("changed", legacy), OtherDefaults, false);
            Assert.Equal(ConfigLoadStatus.Migrated, changed.Status);
            string text = Encoding.ASCII.GetString(changed.Created!);
            Assert.Contains("\r\nToggleKey=F9, Ctrl+Shift+Y\r\n", text);
            // Changed, and equal to what default gives over these defaults, so still written default.
            Assert.Contains("\r\nWorldSpaceYaw=default\r\n", text);
            Assert.Contains("\r\nRotationEnabled=default\r\n", text);
            Assert.Contains("\r\nPositionEnabled=default\r\n", text);
            Assert.Contains("\r\nUdpPort=default\r\n", text);
            Assert.Equal("F9, Ctrl+Shift+Y", changed.Config!.ToggleKeyName);
            Assert.Equal(4343, changed.Config.UdpPort);
            Assert.False(changed.Config.PositionEnabled);

            MigrationOutcome positionOff = MigrationOutcome.Run(
                new DifferentialInput("position off", Edited("\nEnabled = true", "\nEnabled = false")), null, false);
            Assert.Equal(ConfigLoadStatus.Migrated, positionOff.Status);
            string offText = Encoding.ASCII.GetString(positionOff.Created!);
            Assert.Contains("\r\nRotationEnabled=true\r\n", offText);
            Assert.Contains("\r\nPositionEnabled=false\r\n", offText);
        }

        /// <summary>The published build's first-run file with each pair of texts replaced.</summary>
        private static byte[] Edited(params string[] pairs)
        {
            string text = Encoding.ASCII.GetString(Inputs.FirstRun().Bytes!);
            for (int i = 0; i < pairs.Length; i += 2)
            {
                Assert.Contains(pairs[i], text);
                text = text.Replace(pairs[i], pairs[i + 1]);
            }
            return Encoding.ASCII.GetBytes(text);
        }

        private static readonly KeyCode[] Modifiers =
        {
            KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt,
        };

        private static bool IsModifier(KeyCode code)
        {
            return Array.IndexOf(Modifiers, code) >= 0;
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
                if (e[i] != a[i]) lines.Add("  expected " + e[i] + " | got " + a[i]);
            }
            return string.Join("\n", lines);
        }
    }
}
