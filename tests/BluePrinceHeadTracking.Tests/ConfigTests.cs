using System;
using System.IO;
using System.Linq;
using System.Text;
using BluePrinceHeadTracking.Configuration;
using CameraUnlock.Core.Config;
using CameraUnlock.Core.Input;
using CameraUnlock.Core.Tracking;
using Xunit;

namespace BluePrinceHeadTracking.Tests
{
    /// <summary>
    /// The committed config is the table's fresh render byte for byte, and the owner writes the
    /// file only through the rows the tracking mode cycle and the yaw mode toggle persist.
    /// <c>pixi run render-config</c> sets CAMERAUNLOCK_RENDER_CONFIG=write to rewrite the committed
    /// file after a change to a row, a comment or a default.
    /// </summary>
    public class ConfigTests
    {
        private const string CommittedPath = "config/CameraUnlock.ini";
        private const string FileName = "CameraUnlock.ini";
        private const string LegacyName = "com.cameraunlock.blueprince.headtracking.cfg";

        internal static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "pixi.toml"))) dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("no pixi.toml above " + AppDomain.CurrentDomain.BaseDirectory);
            return dir.FullName;
        }

        internal static string Committed()
        {
            return Path.Combine(RepoRoot(), CommittedPath.Replace('/', Path.DirectorySeparatorChar));
        }

        [Fact]
        public void CommittedFileIsTheRenderedDefaults()
        {
            byte[] rendered = BluePrinceConfig.Table().RenderFresh(new RenderHeader(BluePrinceConfig.DisplayName));
            string? mode = Environment.GetEnvironmentVariable("CAMERAUNLOCK_RENDER_CONFIG");
            if (mode == "write")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Committed())!);
                File.WriteAllBytes(Committed(), rendered);
                return;
            }
            Assert.True(string.IsNullOrEmpty(mode), "CAMERAUNLOCK_RENDER_CONFIG is '" + mode + "'; only 'write' is read");
            Assert.True(File.Exists(Committed()), CommittedPath + " is missing; run pixi run render-config");
            Assert.True(rendered.SequenceEqual(File.ReadAllBytes(Committed())),
                CommittedPath + " differs from the table's defaults; run pixi run render-config");
        }

        [Fact]
        public void EveryHotkeyListReadsWithItsChord()
        {
            BluePrinceConfig config = Defaults();

            foreach (string list in new[] { config.ToggleKeyName, config.CycleTrackingModeKeyName, config.YawModeKeyName })
            {
                Assert.True(KeyBindings.TryParse(list, out KeyBinding[] bindings, out string? error), error);
                Assert.Equal(2, bindings.Length);
            }
            Assert.Equal("End, Ctrl+Shift+Y", config.ToggleKeyName);
            Assert.Equal("PageUp, Ctrl+Shift+G", config.CycleTrackingModeKeyName);
            Assert.Equal("PageDown, Ctrl+Shift+H", config.YawModeKeyName);
        }

        [Fact]
        public void DefaultsKeepTheShippedBehaviour()
        {
            BluePrinceConfig config = Defaults();

            Assert.Equal(TrackingMode.RotationAndPosition,
                TrackingModeChannels.Decode(config.RotationEnabled, config.PositionEnabled));
            Assert.True(config.EnableOnStartup);
            Assert.True(config.WorldSpaceYaw);
            Assert.Equal(4242, config.UdpPort);
            Assert.Equal(0.0f, config.LocalSmoothing);
            Assert.Equal(0.15f, config.RemoteSmoothing);
            Assert.Equal(0.0f, config.Position.LocalSmoothing);
            Assert.Equal(0.15f, config.Position.RemoteSmoothing);
            Assert.Equal(0.30f, config.Position.LimitX);
            Assert.Equal(0.20f, config.Position.LimitY);
            Assert.Equal(0.20f, config.Position.LimitYDown);
            Assert.Equal(0.40f, config.Position.LimitZ);
            Assert.Equal(0.10f, config.Position.LimitZBack);
            Assert.True(config.CollisionEnabled);
            Assert.Equal(0.12f, config.CollisionMargin);
            Assert.Equal(0.9f, config.CollisionReleaseSmoothing);
            Assert.True(config.PauseOnLostFocus);
            Assert.False(config.DiagnosticLogging);
        }

        [Fact]
        public void FirstLaunchCreatesTheCommittedBytes()
        {
            using (var dir = new TempDir())
            {
                ConfigLoadResult<BluePrinceConfig> loaded = Owner(dir.Path).Load();

                Assert.Equal(ConfigLoadStatus.Created, loaded.Status);
                Assert.True(File.ReadAllBytes(Path.Combine(dir.Path, FileName)).SequenceEqual(File.ReadAllBytes(Committed())));
                Assert.True(File.Exists(DefaultsPath(dir.Path)));
                Assert.Equal(new[] { FileName, "global" },
                    Directory.GetFileSystemEntries(dir.Path).Select(p => Path.GetFileName(p)!).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            }
        }

        [Fact]
        public void DefaultRowsFollowDefaultsIniAndTheGameKeepsItsCollisionMargin()
        {
            using (var dir = new TempDir())
            {
                Owner(dir.Path).Load();
                string defaults = DefaultsPath(dir.Path);
                string text = File.ReadAllText(defaults);
                Assert.Contains("UdpPort=4242", text);
                Assert.Contains("CollisionEnabled=true", text);
                Assert.Contains("WorldSpaceYaw=true", text);
                Assert.DoesNotContain("CollisionMargin", text);
                File.WriteAllText(defaults, text.Replace("UdpPort=4242", "UdpPort=4343")
                    .Replace("CollisionEnabled=true", "CollisionEnabled=false")
                    .Replace("WorldSpaceYaw=true", "WorldSpaceYaw=false"));

                ConfigLoadResult<BluePrinceConfig> next = Owner(dir.Path).Load();

                Assert.Equal(ConfigLoadStatus.Canonical, next.Status);
                Assert.Equal(4343, next.Config.UdpPort);
                Assert.False(next.Config.CollisionEnabled);
                Assert.False(next.Config.WorldSpaceYaw);
                Assert.Equal(0.12f, next.Config.CollisionMargin);
            }
        }

        [Fact]
        public void TheYawToggleSavesOnlyItsRowAndItComesBack()
        {
            using (var dir = new TempDir())
            {
                string path = Path.Combine(dir.Path, FileName);
                ConfigOwner<BluePrinceConfig> owner = Owner(dir.Path);
                owner.Load();
                string[] created = File.ReadAllLines(path);
                byte[] defaultsBytes = File.ReadAllBytes(DefaultsPath(dir.Path));
                Assert.Contains("WorldSpaceYaw=default", created);

                ConfigSaveResult saved = owner.Save(c => c.WorldSpaceYaw = false);
                Assert.Equal(ConfigSaveStatus.Saved, saved.Status);
                AssertOnlyChanged(created, File.ReadAllLines(path), "WorldSpaceYaw=false");
                Assert.True(File.ReadAllBytes(DefaultsPath(dir.Path)).SequenceEqual(defaultsBytes));

                ConfigLoadResult<BluePrinceConfig> next = Owner(dir.Path).Load();
                Assert.Equal(ConfigLoadStatus.Canonical, next.Status);
                Assert.False(next.Config.WorldSpaceYaw);
            }
        }

        [Fact]
        public void TheModeCycleSavesOnlyThePairAndItComesBack()
        {
            using (var dir = new TempDir())
            {
                string path = Path.Combine(dir.Path, FileName);
                ConfigOwner<BluePrinceConfig> owner = Owner(dir.Path);
                owner.Load();
                string[] created = File.ReadAllLines(path);
                byte[] defaultsBytes = File.ReadAllBytes(DefaultsPath(dir.Path));
                Assert.Contains("RotationEnabled=default", created);
                Assert.Contains("PositionEnabled=default", created);

                TrackingModeChannels.Encode(TrackingMode.PositionOnly, out bool rotation, out bool position);
                ConfigSaveResult saved = owner.Save(c =>
                {
                    c.RotationEnabled = rotation;
                    c.PositionEnabled = position;
                });
                Assert.Equal(ConfigSaveStatus.Saved, saved.Status);
                AssertOnlyChanged(created, File.ReadAllLines(path), "RotationEnabled=false", "PositionEnabled=true");
                Assert.Contains(saved.Log, line => line.Contains("no longer follows Defaults.ini"));
                Assert.True(File.ReadAllBytes(DefaultsPath(dir.Path)).SequenceEqual(defaultsBytes));

                ConfigLoadResult<BluePrinceConfig> next = Owner(dir.Path).Load();
                Assert.Equal(ConfigLoadStatus.Canonical, next.Status);
                Assert.Equal(TrackingMode.PositionOnly,
                    TrackingModeChannels.Decode(next.Config.RotationEnabled, next.Config.PositionEnabled));
            }
        }

        [Fact]
        public void TheOnOffToggleCannotReachTheFile()
        {
            using (var dir = new TempDir())
            {
                ConfigOwner<BluePrinceConfig> owner = Owner(dir.Path);
                owner.Load();
                Assert.Throws<InvalidOperationException>(() => owner.Save(c => c.EnableOnStartup = false));
            }
        }

        [Fact]
        public void TheFileHasNoRowTheModDoesNotUse()
        {
            string text = Encoding.ASCII.GetString(File.ReadAllBytes(Committed()));
            foreach (string gone in new[] { "Sensitivity", "Reticle", "TrueFreeLook", "CollisionChannel", "CollisionRadius",
                         "TrackerPivot", "Light", "[Collision]", "\nEnabled=" })
            {
                Assert.DoesNotContain(gone, text);
            }
        }

        private static BluePrinceConfig Defaults()
        {
            var config = new BluePrinceConfig();
            BluePrinceConfig.Table().Apply(CanonicalIni.Parse(new byte[0]), config);
            return config;
        }

        private static void AssertOnlyChanged(string[] before, string[] after, params string[] expected)
        {
            Assert.Equal(before.Length, after.Length);
            string[] changed = after.Where((line, i) => line != before[i]).ToArray();
            Assert.Equal(expected, changed);
        }

        private static string DefaultsPath(string dir)
        {
            return Path.Combine(dir, "global", "Defaults.ini");
        }

        private static ConfigOwner<BluePrinceConfig> Owner(string dir)
        {
            return new ConfigOwner<BluePrinceConfig>(BluePrinceConfig.Options(
                Path.Combine(dir, FileName), Path.Combine(dir, LegacyName), DefaultsFile.At(DefaultsPath(dir))));
        }

        private sealed class TempDir : IDisposable
        {
            public TempDir()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bp-config-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
