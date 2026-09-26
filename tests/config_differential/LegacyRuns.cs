using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx.Configuration;
using BluePrinceHeadTracking.Configuration;
using BluePrinceHeadTracking.Legacy;
using CameraUnlock.Core.Config.Testing;
using CameraUnlock.Core.Input;
using UnityEngine;

namespace BluePrinceHeadTracking.Tests.Differential
{
    /// <summary>One differential input: a legacy file's bytes, or no file at all.</summary>
    internal sealed class DifferentialInput
    {
        public DifferentialInput(string name, byte[]? bytes)
        {
            Name = name;
            Bytes = bytes;
        }

        public string Name { get; }

        /// <summary>Null for no file.</summary>
        public byte[]? Bytes { get; }
    }

    internal static class Inputs
    {
        public const string LegacyName = "com.cameraunlock.blueprince.headtracking.cfg";

        public static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "pixi.toml"))) dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("no pixi.toml above " + AppDomain.CurrentDomain.BaseDirectory);
            return dir.FullName;
        }

        public static string FirstRunDir()
        {
            return Path.Combine(RepoRoot(), "tests", "config_differential", "data", "first-run");
        }

        /// <summary>
        /// The first-run file of the one published build, the rolling dev pre-release at 6c5513c,
        /// and the base the corpus mutates.
        /// </summary>
        public static DifferentialInput FirstRun()
        {
            string[] files = Directory.GetFiles(FirstRunDir(), "*.cfg");
            if (files.Length != 1) throw new InvalidOperationException(FirstRunDir() + " holds " + files.Length + " first-run files, not 1");
            return new DifferentialInput("first run " + Path.GetFileNameWithoutExtension(files[0]), File.ReadAllBytes(files[0]));
        }

        public static IEnumerable<DifferentialInput> Corpus()
        {
            foreach (IniMutation m in IniMutations.Generate(FirstRun().Bytes!, LegacyConfigKeys.All(), Descriptors()))
            {
                yield return new DifferentialInput("corpus " + m.Name, m.Bytes);
            }
        }

        public static IEnumerable<DifferentialInput> All()
        {
            yield return new DifferentialInput("no file", null);
            yield return new DifferentialInput("empty file", new byte[0]);
            yield return FirstRun();
            foreach (DifferentialInput input in Corpus()) yield return input;
        }

        /// <summary>
        /// A descriptor per key the reader reads, in its order: another valid value, and a value
        /// beyond each end of every AcceptableValueRange, which BepInEx clamps.
        /// </summary>
        public static List<MutationKey> Descriptors()
        {
            var none = new string[0];
            var noChords = new ChordSwitch[0];
            Func<string, string, string, string[], MutationKey> plain =
                (section, key, alternate, outOfRange) => new MutationKey(section, key, alternate, outOfRange, false, noChords);
            Func<string, string, string, MutationKey> hotkey =
                (section, key, alternate) => new MutationKey(section, key, alternate, none, true, noChords);
            return new List<MutationKey>
            {
                plain("Network", "UdpPort", "4343", new[] { "80", "70000" }),
                plain("General", "EnabledOnStartup", "false", none),
                plain("General", "ShowReticle", "false", none),
                plain("General", "WorldSpaceYaw", "false", none),
                plain("General", "PauseOnLostFocus", "false", none),
                plain("General", "DiagnosticLogging", "true", none),
                plain("Smoothing", "LocalSmoothing", "0.3", new[] { "-0.1", "1.5" }),
                plain("Smoothing", "RemoteSmoothing", "0.5", new[] { "-0.1", "1.5" }),
                plain("Position", "Enabled", "false", none),
                plain("Position", "LimitX", "0.25", new[] { "0.001", "0.6" }),
                plain("Position", "LimitY", "0.15", new[] { "0.001", "0.6" }),
                plain("Position", "LimitYDown", "0.12", new[] { "0.001", "0.6" }),
                plain("Position", "LimitZ", "0.35", new[] { "0.001", "0.6" }),
                plain("Position", "LimitZBack", "0.05", new[] { "0.001", "0.6" }),
                plain("Collision", "CollisionEnabled", "false", none),
                plain("Collision", "CollisionRadius", "0.25", new[] { "0.01", "0.6" }),
                plain("Collision", "CollisionReleaseSmoothing", "0.5", new[] { "-0.1", "1.5" }),
                hotkey("Hotkeys", "ToggleKey", "F9"),
                hotkey("Hotkeys", "CycleTrackingModeKey", "F11"),
                hotkey("Hotkeys", "YawModeKey", "F12"),
            };
        }
    }

    /// <summary>
    /// A scratch folder holding at most the legacy file. <see cref="With{T}"/> deletes it only once
    /// the run inside has returned, so an exception from the run reaches the test as it was thrown,
    /// and the folder it failed in is left behind to look at.
    /// </summary>
    internal sealed class LegacyFolder
    {
        private LegacyFolder(DifferentialInput input)
        {
            Path = Scratch.Create("bp-diff-");
            LegacyPath = System.IO.Path.Combine(Path, Inputs.LegacyName);
            if (input.Bytes != null) File.WriteAllBytes(LegacyPath, input.Bytes);
        }

        public static T With<T>(DifferentialInput input, Func<LegacyFolder, T> run)
        {
            var folder = new LegacyFolder(input);
            T result = run(folder);
            Scratch.Delete(folder.Path);
            return result;
        }

        public string Path { get; }

        public string LegacyPath { get; }

        public string[] Entries()
        {
            string[] names = Directory.GetFileSystemEntries(Path).Select(p => System.IO.Path.GetFileName(p)!).ToArray();
            Array.Sort(names, StringComparer.Ordinal);
            return names;
        }
    }

    internal static class Scratch
    {
        private const int DeleteAttempts = 20;

        public static string Create(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Windows can hold a file open for a moment after the process that wrote it closed it, a
        /// virus scanner reading it being the usual reason, and a folder delete then fails with a
        /// sharing violation or on a part of the path already gone. Thousands of runs in parallel
        /// hit that often enough to fail a build, so the delete is tried again, for about five
        /// seconds, until the folder is gone.
        /// </summary>
        public static void Delete(string path)
        {
            for (int attempt = 1; Directory.Exists(path); attempt++)
            {
                try
                {
                    foreach (string file in Directory.GetFiles(path)) File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(path, true);
                }
                catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt < DeleteAttempts)
                {
                    Thread.Sleep(attempt * 25);
                }
            }
        }
    }

    /// <summary>
    /// What one run of a legacy reader gave: the settings, or the exception BepInEx threw reading
    /// the file, which in game stops the plugin loading at all.
    /// </summary>
    internal sealed class LegacyOutcome
    {
        public LegacyConfig? Config;
        public bool Found;
        public string? Error;

        public string Describe()
        {
            if (Error != null) return "throws " + Error;
            return "found=" + LegacyStartup.Text(Found) + "\n" + LegacyStartup.Fields(Config!);
        }

        public static string ErrorOf(Exception e)
        {
            return e.GetType().FullName + ": " + e.Message;
        }
    }

    /// <summary>
    /// What the published build ran on for one input, read by its own PluginConfig on a ConfigFile
    /// built as BepInEx 6's BasePlugin builds the plugin's, which reads an existing file at once.
    /// </summary>
    internal static class Oracle
    {
        public static LegacyOutcome Run(DifferentialInput input)
        {
            return LegacyFolder.With(input, folder =>
            {
                var published = new PluginConfig();
                try
                {
                    published.Initialize(new ConfigFile(folder.LegacyPath, false));
                }
                catch (ArgumentException e)
                {
                    return new LegacyOutcome { Error = LegacyOutcome.ErrorOf(e) };
                }
                return new LegacyOutcome
                {
                    Found = input.Bytes != null,
                    Config = new LegacyConfig
                    {
                        UdpPort = published.UdpPort.Value,
                        EnabledOnStartup = published.EnabledOnStartup.Value,
                        ShowReticle = published.ShowReticle.Value,
                        WorldSpaceYaw = published.WorldSpaceYaw.Value,
                        PauseOnLostFocus = published.PauseOnLostFocus.Value,
                        DiagnosticLogging = published.DiagnosticLogging.Value,
                        LocalSmoothing = published.LocalSmoothing.Value,
                        RemoteSmoothing = published.RemoteSmoothing.Value,
                        PositionEnabled = published.PositionEnabled.Value,
                        PositionLimitX = published.PositionLimitX.Value,
                        PositionLimitY = published.PositionLimitY.Value,
                        PositionLimitYDown = published.PositionLimitYDown.Value,
                        PositionLimitZ = published.PositionLimitZ.Value,
                        PositionLimitZBack = published.PositionLimitZBack.Value,
                        CollisionEnabled = published.CollisionEnabled.Value,
                        CollisionRadius = published.CollisionRadius.Value,
                        CollisionReleaseSmoothing = published.CollisionReleaseSmoothing.Value,
                        ToggleKey = published.ToggleKey.Value,
                        CycleTrackingModeKey = published.CycleTrackingModeKey.Value,
                        YawModeKey = published.YawModeKey.Value,
                    },
                };
            });
        }
    }

    /// <summary>
    /// The frozen reader on one input, on a ConfigFile built as the plugin's is. It must leave the
    /// file and its folder as they were.
    /// </summary>
    internal static class FrozenReader
    {
        public static LegacyOutcome Run(DifferentialInput input)
        {
            return LegacyFolder.With(input, folder =>
            {
                string[] before = folder.Entries();
                DateTime written = input.Bytes == null ? DateTime.MinValue : File.GetLastWriteTimeUtc(folder.LegacyPath);

                var outcome = new LegacyOutcome();
                try
                {
                    outcome.Config = LegacyConfigReader.Read(new ConfigFile(folder.LegacyPath, false), out outcome.Found);
                }
                catch (ArgumentException e)
                {
                    outcome.Error = LegacyOutcome.ErrorOf(e);
                }

                if (!before.SequenceEqual(folder.Entries()))
                    throw new InvalidOperationException(input.Name + ": the frozen reader changed the folder: " + string.Join(", ", folder.Entries()));
                if (input.Bytes != null)
                {
                    if (!File.ReadAllBytes(folder.LegacyPath).SequenceEqual(input.Bytes))
                        throw new InvalidOperationException(input.Name + ": the frozen reader rewrote the legacy file");
                    if (File.GetLastWriteTimeUtc(folder.LegacyPath) != written)
                        throw new InvalidOperationException(input.Name + ": the frozen reader touched the legacy file");
                }
                return outcome;
            });
        }
    }

    /// <summary>
    /// What the published HeadTrackingPlugin.Load, HeadTrackingBehaviour.Initialize and
    /// HotkeyHandler set up from its settings, one line per item, floats with their bits.
    /// </summary>
    internal static class LegacyStartup
    {
        public static SortedDictionary<string, string> Of(LegacyConfig c)
        {
            var s = new SortedDictionary<string, string>(StringComparer.Ordinal);
            s["TrackingEnabled"] = Text(c.EnabledOnStartup);
            s["RotationEnabled"] = "true";
            s["PositionEnabled"] = Text(c.PositionEnabled);
            s["WorldSpaceYaw"] = Text(c.WorldSpaceYaw);
            s["UdpPort"] = c.UdpPort.ToString(CultureInfo.InvariantCulture);
            s["CrosshairFollowsAim"] = Text(c.ShowReticle);
            s["PauseOnLostFocus"] = Text(c.PauseOnLostFocus);
            s["DiagnosticLogging"] = Text(c.DiagnosticLogging);
            s["LocalSmoothing"] = Text(c.LocalSmoothing);
            s["RemoteSmoothing"] = Text(c.RemoteSmoothing);
            s["PositionLimits"] = Text(c.PositionLimitX) + " " + Text(c.PositionLimitY) + " " + Text(c.PositionLimitYDown)
                                  + " " + Text(c.PositionLimitZ) + " " + Text(c.PositionLimitZBack);
            s["CollisionEnabled"] = Text(c.CollisionEnabled);
            s["CollisionMargin"] = Text(c.CollisionRadius);
            s["CollisionReleaseSmoothing"] = Text(c.CollisionReleaseSmoothing);
            s["ToggleKey"] = Hotkey(c.ToggleKey, KeyCode.Y);
            s["CycleTrackingModeKey"] = Hotkey(c.CycleTrackingModeKey, KeyCode.G);
            s["YawModeKey"] = Hotkey(c.YawModeKey, KeyCode.H);
            return s;
        }

        /// <summary>Every field, floats with their bits.</summary>
        public static string Fields(LegacyConfig c)
        {
            var text = new StringBuilder();
            foreach (FieldInfo field in typeof(LegacyConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object? value = field.GetValue(c);
                string? shown = value is float f ? Text(f)
                    : value is KeyCode k ? ((int)k).ToString(CultureInfo.InvariantCulture) + " " + k
                    : Convert.ToString(value, CultureInfo.InvariantCulture);
                text.Append(field.Name).Append('=').Append(shown).Append('\n');
            }
            return text.ToString();
        }

        public static string Text(bool value)
        {
            return value ? "true" : "false";
        }

        public static string Text(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture) + "/0x"
                   + BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The bindings ChordHotkeys.IsActionPressed(primary, letter) fired on: the primary key
        /// whatever else was held, unless it was None, and Ctrl+Shift+letter.
        /// </summary>
        public static string Hotkey(KeyCode primary, KeyCode chordLetter)
        {
            string chord = KeyBindings.Format(new[] { new KeyBinding(KeyModifiers.Ctrl | KeyModifiers.Shift, (int)chordLetter) });
            if (primary == KeyCode.None) return chord;
            return KeyName((int)primary) + ", " + chord;
        }

        /// <summary>A Unity key code's name, or the number for one that names no key.</summary>
        public static string KeyName(int unityKeyCode)
        {
            try
            {
                return KeyBindings.Format(new[] { new KeyBinding(KeyModifiers.None, unityKeyCode) });
            }
            catch (ArgumentException)
            {
                return unityKeyCode.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
