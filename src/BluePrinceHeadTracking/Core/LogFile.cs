// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;

namespace BluePrinceHeadTracking.Core;

/// <summary>
/// Mirrors this plugin's log lines into HeadTracking.log next to the game
/// executable, where a user can find them without knowing where BepInEx keeps its
/// own log. Truncated on every launch, so it always holds the current session and
/// never grows without bound.
/// </summary>
internal sealed class LogFile : ILogListener
{
    internal const string FileName = "HeadTracking.log";

    private readonly string _sourceName;
    private readonly StreamWriter _writer;

    // BepInEx dispatches to listeners on whichever thread logged, and this plugin
    // logs from more than one: OpenTrackReceiver's Log callback fires from its UDP
    // receive thread alongside every main-thread line. StreamWriter is not
    // thread-safe - concurrent writes corrupt its char buffer and throw out of a
    // background thread, which on the receive thread means tracking dies with no
    // line explaining it. The same lock closes the shutdown race, where an
    // in-flight write lands on a writer Dispose() has already closed.
    private readonly object _writeLock = new();
    private bool _disposed;

    public LogLevel LogLevelFilter => LogLevel.All;

    private LogFile(string sourceName, StreamWriter writer)
    {
        _sourceName = sourceName;
        _writer = writer;
    }

    /// <summary>
    /// Opens the mirror, or returns null when the game directory will not take the
    /// file - a game installed somewhere the player cannot write is the case that
    /// reaches this. The mirror is a convenience over BepInEx's own log, so it says
    /// why it is absent and the mod carries on; letting the open throw took every
    /// bit of head tracking down with it, from inside <c>Load</c>, over a log file.
    /// </summary>
    internal static LogFile? Attach(string sourceName, string header)
    {
        string path = Path.Combine(Paths.GameRootPath, FileName);

        FileStream? stream = null;
        StreamWriter writer;
        try
        {
            // FileShare.ReadWrite so tailing the file from another process does not
            // stop the next launch from opening it.
            stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            writer = new StreamWriter(stream) { AutoFlush = true };
            writer.WriteLine($"{header} - session started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that opened and then failed to write still holds a handle.
            stream?.Dispose();
            HeadTrackingPlugin.Logger.LogWarning(
                $"Could not open {path} ({ex.GetType().Name}: {ex.Message}) - head tracking runs as " +
                "normal, but its log lines are only in BepInEx/LogOutput.log");
            return null;
        }

        var listener = new LogFile(sourceName, writer);
        BepInEx.Logging.Logger.Listeners.Add(listener);
        return listener;
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if (eventArgs.Source.SourceName != _sourceName) return;

        string line = $"[{DateTime.Now:HH:mm:ss}] [{eventArgs.Level}] {eventArgs.Data}";
        lock (_writeLock)
        {
            if (_disposed) return;
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        BepInEx.Logging.Logger.Listeners.Remove(this);

        lock (_writeLock)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }
}
