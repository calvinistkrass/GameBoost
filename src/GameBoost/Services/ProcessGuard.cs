using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using GameBoost.Infrastructure;

namespace GameBoost.Services;

public sealed record ProcessCheckResult(bool Allowed, string? Path, string? Reason);

/// <summary>
/// Sicherheitsregeln: Entscheidet, ob ein Prozess beendet werden darf.
/// Erlaubt sind ausschließlich Prozesse des aktuellen Benutzers in der aktuellen Sitzung,
/// die nicht auf der Blacklist stehen und nicht aus dem Windows-Verzeichnis stammen.
/// </summary>
public static class ProcessGuard
{
    /// <summary>System-, Windows-, Sicherheits- und Anti-Cheat-Prozesse, die niemals beendet werden.</summary>
    private static readonly HashSet<string> Blacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kernel / Sitzung
        "system", "idle", "registry", "memory compression", "secure system", "smss", "csrss", "wininit",
        "winlogon", "services", "lsass", "lsaiso", "svchost", "fontdrvhost", "dwm", "userinit", "logonui",
        "lockapp", "sihost", "taskhostw", "ctfmon", "conhost", "openconsole", "dllhost", "rundll32",
        "audiodg", "spoolsv", "wudfhost", "wmiprvse", "dashost", "unsecapp", "backgroundtaskhost",
        "runtimebroker", "comppkgsrv", "sppsvc", "tiworker", "trustedinstaller", "mousocoreworker",
        "wlanext", "useroobebroker", "smartscreen", "sgrmbroker", "werfault", "wermgr", "consent",
        // Shell / Oberfläche
        "explorer", "searchhost", "searchapp", "searchui", "searchindexer", "searchprotocolhost",
        "searchfilterhost", "startmenuexperiencehost", "shellexperiencehost", "shellhost", "textinputhost",
        "applicationframehost", "systemsettings", "systemsettingsbroker", "widgets", "widgetservice",
        "taskmgr", "msedgewebview2", "windowsinternal.composableshell.experiences.textinput.inputapp",
        // Sicherheit
        "msmpeng", "mpdefendercoreservice", "nissrv", "securityhealthsystray", "securityhealthservice",
        "securityhealthhost", "mpcmdrun",
        // Anti-Cheat (Beenden würde das laufende Spiel stören)
        "vgc", "vgk", "vgtray", "easyanticheat", "easyanticheat_eos", "eac_launcher", "beservice",
        "beservice_x64", "battleye", "faceit", "faceitservice", "faceitclient", "ricochet",
        // GameBoost selbst
        "gameboost"
    };

    private static readonly int CurrentSessionId = Process.GetCurrentProcess().SessionId;
    private static readonly SecurityIdentifier? CurrentUserSid = WindowsIdentity.GetCurrent().User;
    private static readonly string[] ProtectedDirectories = BuildProtectedDirectories();

    public static bool IsProtectedName(string processName) => Blacklist.Contains(processName.Trim());

    public static bool IsProtectedPath(string path)
    {
        foreach (var dir in ProtectedDirectories)
        {
            if (path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static ProcessCheckResult Check(Process process)
    {
        try
        {
            if (process.Id == Environment.ProcessId) return Deny("eigener Prozess");
            if (process.Id <= 4) return Deny("Systemprozess");
            if (IsProtectedName(process.ProcessName)) return Deny("geschützter Name");
            if (process.SessionId != CurrentSessionId) return Deny("andere Sitzung");

            var path = QueryPathAndOwner(process.Id, out var owner);
            if (path == null) return Deny("kein Zugriff");
            if (IsProtectedPath(path)) return Deny("Windows-Verzeichnis");
            if (owner == null || CurrentUserSid == null || !owner.Equals(CurrentUserSid)) return Deny("anderer Benutzer");

            return new ProcessCheckResult(true, path, null);
        }
        catch (Exception ex)
        {
            return Deny("Fehler: " + ex.Message);
        }
    }

    private static ProcessCheckResult Deny(string reason) => new(false, null, reason);

    private static string[] BuildProtectedDirectories()
    {
        var list = new List<string>();
        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            list.Add(dir.TrimEnd('\\') + "\\");
        }

        Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        Add(Path.Combine(pf, "Windows Defender"));
        Add(Path.Combine(pf, "Windows Defender Advanced Threat Protection"));
        Add(Path.Combine(pf, "Windows Security"));
        Add(Path.Combine(pd, "Microsoft", "Windows Defender"));
        return list.ToArray();
    }

    /// <summary>Liest Pfad und Besitzer-SID eines Prozesses (ohne Adminrechte).</summary>
    private static string? QueryPathAndOwner(int pid, out SecurityIdentifier? owner)
    {
        owner = null;
        var hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero) return null;

        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            string? path = NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size) ? sb.ToString() : null;

            if (NativeMethods.OpenProcessToken(hProcess, NativeMethods.TOKEN_QUERY, out var hToken))
            {
                try
                {
                    owner = GetTokenUser(hToken);
                }
                finally
                {
                    NativeMethods.CloseHandle(hToken);
                }
            }

            return path;
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    private static SecurityIdentifier? GetTokenUser(IntPtr hToken)
    {
        NativeMethods.GetTokenInformation(hToken, NativeMethods.TokenUser, IntPtr.Zero, 0, out var length);
        if (length <= 0) return null;

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!NativeMethods.GetTokenInformation(hToken, NativeMethods.TokenUser, buffer, length, out _)) return null;
            // TOKEN_USER beginnt mit SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes; }
            var sidPtr = Marshal.ReadIntPtr(buffer);
            return new SecurityIdentifier(sidPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
