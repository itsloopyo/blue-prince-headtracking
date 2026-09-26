using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using BluePrinceHeadTracking.Configuration;
using BluePrinceHeadTracking.Legacy;
using CameraUnlock.Core.Config;

namespace BluePrinceHeadTracking.Tests.Differential
{
    /// <summary>The import on one input: the frozen reader on a ConfigFile over the legacy file, then the map.</summary>
    internal sealed class ImportOutcome
    {
        public string? Error;
        public ImportResult? Result;
        public BluePrinceConfig? Config;

        public static ImportOutcome Run(DifferentialInput input)
        {
            return LegacyFolder.With(input, folder =>
            {
                ConfigFile file;
                try
                {
                    file = new ConfigFile(folder.LegacyPath, false);
                }
                catch (ArgumentException e)
                {
                    return new ImportOutcome { Error = LegacyOutcome.ErrorOf(e) };
                }
                // As the owner seeds it: the table's built-in values on the rows the map leaves alone.
                var config = new BluePrinceConfig();
                BluePrinceConfig.Table().Apply(CanonicalIni.Parse(new byte[0]), config);
                ImportResult result = LegacyConfigImport.Run(file, new LegacyImportInput(folder.LegacyPath), config);
                return new ImportOutcome { Result = result, Config = config };
            });
        }
    }

    /// <summary>
    /// The migration on one input: the owner's Load in a folder holding only the legacy file, then
    /// a second Load over the same Defaults.ini. Every check the design asks of the files and the
    /// folder is made here, and a broken one throws.
    /// </summary>
    internal sealed class MigrationOutcome
    {
        public string? Error;
        public ConfigLoadStatus Status;
        public BluePrinceConfig? Config;
        public byte[]? Created;
        public string? Reason;
        public IList<string> Log = new List<string>();

        /// <param name="defaultsIni">What Defaults.ini holds before the load, or null for none, so
        /// the owner creates it with the built-in values.</param>
        public static MigrationOutcome Run(DifferentialInput input, string? defaultsIni, bool readOnly)
        {
            string defaultsDir = Scratch.Create("bp-defaults-");
            string defaultsPath = Path.Combine(defaultsDir, "Defaults.ini");
            if (defaultsIni != null) File.WriteAllText(defaultsPath, defaultsIni, Encoding.ASCII);
            MigrationOutcome outcome = LegacyFolder.With(input, folder => Run(input, folder, DefaultsFile.At(defaultsPath), readOnly));
            Scratch.Delete(defaultsDir);
            return outcome;
        }

        private static MigrationOutcome Run(DifferentialInput input, LegacyFolder folder, DefaultsFile defaults, bool readOnly)
        {
            string configPath = Path.Combine(folder.Path, "CameraUnlock.ini");
            DateTime written = DateTime.MinValue;
            if (input.Bytes != null)
            {
                if (readOnly) File.SetAttributes(folder.LegacyPath, FileAttributes.ReadOnly);
                written = File.GetLastWriteTimeUtc(folder.LegacyPath);
            }

            // The loader builds the plugin's Config, which reads the file, before Load runs.
            try
            {
                new ConfigFile(folder.LegacyPath, false);
            }
            catch (ArgumentException e)
            {
                return new MigrationOutcome { Error = LegacyOutcome.ErrorOf(e) };
            }

            ConfigLoadResult<BluePrinceConfig> loaded = Owner(folder, defaults).Load();
            var outcome = new MigrationOutcome
            {
                Status = loaded.Status,
                Config = loaded.Config,
                Reason = loaded.Reason,
                Log = loaded.Log,
            };
            CheckLegacyFile(input, folder, written, readOnly);

            string[] expected;
            if (loaded.Status == ConfigLoadStatus.Migrated || loaded.Status == ConfigLoadStatus.Created)
            {
                expected = input.Bytes == null ? new[] { "CameraUnlock.ini" } : new[] { "CameraUnlock.ini", Inputs.LegacyName };
                outcome.Created = File.ReadAllBytes(configPath);
            }
            else
            {
                expected = input.Bytes == null ? new string[0] : new[] { Inputs.LegacyName };
            }
            if (!expected.SequenceEqual(folder.Entries()))
                throw new InvalidOperationException(input.Name + ": " + loaded.Status + " left " + string.Join(", ", folder.Entries()));

            if (outcome.Created != null)
            {
                DateTime createdAt = File.GetLastWriteTimeUtc(configPath);
                ConfigLoadResult<BluePrinceConfig> second = Owner(folder, defaults).Load();
                if (second.Status != ConfigLoadStatus.Canonical)
                    throw new InvalidOperationException(input.Name + ": the second load is " + second.Status);
                if (Describe(second.Config) != Describe(loaded.Config))
                    throw new InvalidOperationException(input.Name + ": the second load reads another config:\n" + Describe(second.Config));
                if (input.Bytes != null && !second.Log.Any(l => l.Contains("settings are read from this file") && l.Contains("is not read")))
                    throw new InvalidOperationException(input.Name + ": the second load does not say the legacy file is not read");
                if (!File.ReadAllBytes(configPath).SequenceEqual(outcome.Created) || File.GetLastWriteTimeUtc(configPath) != createdAt)
                    throw new InvalidOperationException(input.Name + ": the second load rewrote CameraUnlock.ini");
                CheckLegacyFile(input, folder, written, readOnly);
            }
            return outcome;
        }

        private static void CheckLegacyFile(DifferentialInput input, LegacyFolder folder, DateTime written, bool readOnly)
        {
            if (input.Bytes == null) return;
            if (!File.ReadAllBytes(folder.LegacyPath).SequenceEqual(input.Bytes))
                throw new InvalidOperationException(input.Name + ": the legacy file's bytes changed");
            if (File.GetLastWriteTimeUtc(folder.LegacyPath) != written)
                throw new InvalidOperationException(input.Name + ": the legacy file's write time changed");
            bool isReadOnly = (File.GetAttributes(folder.LegacyPath) & FileAttributes.ReadOnly) != 0;
            if (isReadOnly != readOnly)
                throw new InvalidOperationException(input.Name + ": the legacy file's read-only attribute changed");
        }

        private static ConfigOwner<BluePrinceConfig> Owner(LegacyFolder folder, DefaultsFile defaults)
        {
            return new ConfigOwner<BluePrinceConfig>(
                BluePrinceConfig.Options(Path.Combine(folder.Path, "CameraUnlock.ini"), folder.LegacyPath, defaults));
        }

        /// <summary>Every setting a row of the table holds, floats with their bits.</summary>
        public static string Describe(BluePrinceConfig c)
        {
            var s = new StringBuilder();
            Action<string, string> line = (name, value) => s.Append(name).Append('=').Append(value).Append('\n');
            line("UdpPort", c.UdpPort.ToString(CultureInfo.InvariantCulture));
            line("EnableOnStartup", LegacyStartup.Text(c.EnableOnStartup));
            line("WorldSpaceYaw", LegacyStartup.Text(c.WorldSpaceYaw));
            line("RotationEnabled", LegacyStartup.Text(c.RotationEnabled));
            line("PositionEnabled", LegacyStartup.Text(c.PositionEnabled));
            line("LocalSmoothing", LegacyStartup.Text(c.LocalSmoothing));
            line("RemoteSmoothing", LegacyStartup.Text(c.RemoteSmoothing));
            line("Position.LocalSmoothing", LegacyStartup.Text(c.Position.LocalSmoothing));
            line("Position.RemoteSmoothing", LegacyStartup.Text(c.Position.RemoteSmoothing));
            line("PositionLimitX", LegacyStartup.Text(c.Position.LimitX));
            line("PositionLimitY", LegacyStartup.Text(c.Position.LimitY));
            line("PositionLimitYDown", LegacyStartup.Text(c.Position.LimitYDown));
            line("PositionLimitZ", LegacyStartup.Text(c.Position.LimitZ));
            line("PositionLimitZBack", LegacyStartup.Text(c.Position.LimitZBack));
            line("CollisionEnabled", LegacyStartup.Text(c.CollisionEnabled));
            line("CollisionMargin", LegacyStartup.Text(c.CollisionMargin));
            line("CollisionReleaseSmoothing", LegacyStartup.Text(c.CollisionReleaseSmoothing));
            line("ToggleKey", c.ToggleKeyName);
            line("CycleTrackingModeKey", c.CycleTrackingModeKeyName);
            line("YawModeKey", c.YawModeKeyName);
            line("PauseOnLostFocus", LegacyStartup.Text(c.PauseOnLostFocus));
            line("DiagnosticLogging", LegacyStartup.Text(c.DiagnosticLogging));
            return s.ToString();
        }
    }

    /// <summary>
    /// What the converted plugin sets up from its settings, in the same terms as
    /// <see cref="LegacyStartup"/>: the game's pointer always follows the aim.
    /// </summary>
    internal static class ConvertedStartup
    {
        public static SortedDictionary<string, string> Of(BluePrinceConfig c)
        {
            var s = new SortedDictionary<string, string>(StringComparer.Ordinal);
            s["TrackingEnabled"] = LegacyStartup.Text(c.EnableOnStartup);
            s["RotationEnabled"] = LegacyStartup.Text(c.RotationEnabled);
            s["PositionEnabled"] = LegacyStartup.Text(c.PositionEnabled);
            s["WorldSpaceYaw"] = LegacyStartup.Text(c.WorldSpaceYaw);
            s["UdpPort"] = c.UdpPort.ToString(CultureInfo.InvariantCulture);
            s["CrosshairFollowsAim"] = "true";
            s["PauseOnLostFocus"] = LegacyStartup.Text(c.PauseOnLostFocus);
            s["DiagnosticLogging"] = LegacyStartup.Text(c.DiagnosticLogging);
            s["LocalSmoothing"] = LegacyStartup.Text(c.LocalSmoothing);
            s["RemoteSmoothing"] = LegacyStartup.Text(c.RemoteSmoothing);
            s["PositionLimits"] = LegacyStartup.Text(c.Position.LimitX) + " " + LegacyStartup.Text(c.Position.LimitY) + " "
                                  + LegacyStartup.Text(c.Position.LimitYDown) + " " + LegacyStartup.Text(c.Position.LimitZ) + " "
                                  + LegacyStartup.Text(c.Position.LimitZBack);
            s["CollisionEnabled"] = LegacyStartup.Text(c.CollisionEnabled);
            s["CollisionMargin"] = LegacyStartup.Text(c.CollisionMargin);
            s["CollisionReleaseSmoothing"] = LegacyStartup.Text(c.CollisionReleaseSmoothing);
            s["ToggleKey"] = c.ToggleKeyName;
            s["CycleTrackingModeKey"] = c.CycleTrackingModeKeyName;
            s["YawModeKey"] = c.YawModeKeyName;
            return s;
        }
    }
}
