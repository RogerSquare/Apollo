using System;
using System.IO;

// Adapted from Handoff (github.com/RogerSquare/handoff).
namespace ApolloTray;

/// <summary>Append-only log at %LOCALAPPDATA%\ApolloTray\apollotray.log, rotated at 2 MB.</summary>
public static class Log
{
    static readonly object Gate = new();
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApolloTray");
    public static readonly string FilePath = Path.Combine(Dir, "apollotray.log");

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) =>
        Write("ERROR", ex == null ? msg : $"{msg}: {ex.GetType().Name}: {ex.Message}");

    static void Write(string level, string msg)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 2_000_000)
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {msg}{Environment.NewLine}");
            }
            catch { /* logging must never take the app down */ }
        }
    }
}
