using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using GameBoost.Infrastructure;

namespace GameBoost.Services;

public sealed record ProcessCheckResult(bool Allowed, string? Path, string? Reason);

/// <summary>
/// Safety rules: decides whether a process may be closed.
/// Only processes of the current user in the current session are allowed,
/// that are not blacklisted and do not come from the Windows directory.
/// </summary>
public static class ProcessGuard
{
    /// <summary>System, Windows, security and anti-cheat processes that are never closed.</summary>
    private static readonly HashSet<string> Blacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kernel / session
        "system", "idle", "registry", "memory compression", "secure system", "smss", "csrss", "wininit",
        "winlogon", "services", "lsass", "lsaiso", "svchost", "fontdrvhost", "dwm", "userinit", "logonui",
        "lockapp", "sihost", "taskhostw", "ctfmon", "conhost", "openconsole", "dllhost", "rundll32",
        "audiodg", "spoolsv", "wudfhost", "wmiprvse", "dashost", "unsecapp", "backgroundtaskhost",
        "runtimebroker", "comppkgsrv", "sppsvc", "tiworker", "trustedinstaller", "mousocoreworker",
        "wlanext", "useroobebroker", "smartscreen", "sgrmbroker", "werfault", "wermgr", "consent",
        // Shell / UI
        "explorer", "searchhost", "searchapp", "searchui", "searchindexer", "searchprotocolhost",
        "searchfilterhost", "startmenuexperiencehost", "shellexperiencehost", "shellhost", "textinputhost",
        "applicationframehost", "systemsettings", "systemsettingsbroker", "widgets", "widgetservice",
        "taskmgr", "msedgewebview2", "windowsinternal.composableshell.experiences.textinput.inputapp",
        // Security
        "msmpeng", "mpdefendercoreservice", "nissrv", "securityhealthsystray", "securityhealthservice",
        "securityhealthhost", "mpcmdrun",
        // Anti-cheat (closing it would break the running game)
        "vgc", "vgk", "vgtray", "easyanticheat", "easyanticheat_eos", "eac_launcher", "beservice",
        "beservice_x64", "battleye", "faceit", "faceitservice", "faceitclient", "ricochet",
        // GameBoost itself
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
            if (process.Id == Environment.ProcessId) return Deny("own process");
            if (process.Id <= 4) return Deny("system process");
            if (IsProtectedName(process.ProcessName)) return Deny("protected name");
            if (process.SessionId != CurrentSessionId) return Deny("other session");

            var path = QueryPathAndOwner(process.Id, out var owner);
            if (path == null) return Deny("no access");
            if (IsProtectedPath(path)) return Deny("Windows directory");
            if (owner == null || CurrentUserSid == null || !owner.Equals(CurrentUserSid)) return Deny("other user");

            return new ProcessCheckResult(true, path, null);
        }
        catch (Exception ex)
        {
            return Deny("error: " + ex.Message);
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

    /// <summary>Reads a process's path and owner SID (without admin rights).</summary>
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
            // TOKEN_USER starts with SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes; }
            var sidPtr = Marshal.ReadIntPtr(buffer);
            return new SecurityIdentifier(sidPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
