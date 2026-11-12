namespace Workbench.Host.Sources.Claude;

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;

internal sealed class WindowsClaudeProcessReader : IClaudeProcessReader
{
    public ClaudeProcessInventory Capture()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new(false, new Dictionary<int, ClaudeProcessInventory.ProcessIdentity>());
        }

        try
        {
            return ReadWindowsSnapshot();
        }
        catch (Exception error) when (error is Win32Exception or UnauthorizedAccessException or InvalidOperationException or SecurityException)
        {
            return new(false, new Dictionary<int, ClaudeProcessInventory.ProcessIdentity>());
        }
    }

    private static ClaudeProcessInventory ReadWindowsSnapshot()
    {
        using var snapshot = NativeMethods.CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid)
        {
            return new(false, new Dictionary<int, ClaudeProcessInventory.ProcessIdentity>());
        }

        var entry = new NativeMethods.ProcessEntry { Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry>() };
        var native = new Dictionary<int, (int ParentId, string Name)>();
        if (!NativeMethods.Process32First(snapshot, ref entry))
        {
            return new(false, new Dictionary<int, ClaudeProcessInventory.ProcessIdentity>());
        }

        do
        {
            native[(int)entry.ProcessId] = ((int)entry.ParentProcessId, entry.ExecutableName ?? string.Empty);
        }
        while (NativeMethods.Process32Next(snapshot, ref entry));

        var complete = Marshal.GetLastWin32Error() == 18;
        var selected = new Dictionary<int, ClaudeProcessInventory.ProcessIdentity>();
        foreach (var candidate in native.Where(row => row.Value.Name.Equals("claude.exe", StringComparison.OrdinalIgnoreCase)))
        {
            var current = candidate.Key;
            var visited = new HashSet<int>();
            for (var depth = 0; current != 0 && depth < 64; depth++)
            {
                if (selected.ContainsKey(current))
                {
                    break;
                }

                if (!visited.Add(current) || !native.TryGetValue(current, out var row))
                {
                    complete = false;
                    break;
                }

                var identity = ReadIdentity(current, row.ParentId, row.Name);
                selected[current] = identity;
                if (identity.CreatedAt is null)
                {
                    complete = false;
                }

                if (depth > 0 && identity.Role is ClaudeProcessInventory.ProcessRole.DesktopApp or ClaudeProcessInventory.ProcessRole.Terminal)
                {
                    break;
                }

                current = row.ParentId;
                if (depth == 63 && current != 0)
                {
                    complete = false;
                }
            }
        }

        return new(complete, selected);
    }

    private static ClaudeProcessInventory.ProcessIdentity ReadIdentity(int pid, int parent, string name)
    {
        var isClaude = name.Equals("claude.exe", StringComparison.OrdinalIgnoreCase);
        var role = isClaude
            ? ClaudeProcessInventory.ProcessRole.Claude
            : name.ToLowerInvariant() switch
            {
                "windowsterminal.exe" or "wt.exe" or "conhost.exe" or "openconsole.exe" or
                "pwsh.exe" or "powershell.exe" or "cmd.exe" => ClaudeProcessInventory.ProcessRole.Terminal,
                _ => ClaudeProcessInventory.ProcessRole.Other,
            };
        try
        {
            using var process = Process.GetProcessById(pid);
            _ = process.SafeHandle;
            if (!name.Equals(process.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return new(pid, null, parent, role, isClaude);
            }

            var createdAt = new DateTimeOffset(process.StartTime.ToUniversalTime());
            if (isClaude)
            {
                var path = process.MainModule?.FileName;
                if (path is null)
                {
                    return new(pid, null, parent, role, isClaude);
                }

                if (path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
                {
                    role = ClaudeProcessInventory.ProcessRole.DesktopApp;
                }
            }

            process.Refresh();
            if (process.HasExited ||
                process.StartTime.ToUniversalTime() != createdAt.UtcDateTime ||
                !name.Equals(process.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return new(pid, null, parent, role, isClaude);
            }

            return new(pid, createdAt, parent, role, isClaude);
        }
        catch (Exception error) when (error is Win32Exception or ArgumentException or InvalidOperationException or UnauthorizedAccessException or SecurityException or NotSupportedException)
        {
            return new(pid, null, parent, role, isClaude);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ProcessEntry
        {
            internal uint Size;
            internal uint Usage;
            internal uint ProcessId;
            internal nuint DefaultHeap;
            internal uint ModuleId;
            internal uint ThreadCount;
            internal uint ParentProcessId;
            internal int BasePriority;
            internal uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            internal string? ExecutableName;
        }
    }
}
