using System.Runtime.InteropServices;

namespace Agent.PInvoke;

internal static class ScmNativeMethods
{
    private const string Advapi32 = "advapi32.dll";

    [DllImport(Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr OpenSCManager(
        string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport(Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr OpenServiceW(
        IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport(Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateService(
        IntPtr hSCManager, string lpServiceName, string lpDisplayName,
        uint dwDesiredAccess, uint dwServiceType, uint dwStartType,
        uint dwErrorControl, string lpBinaryPathName, string? lpLoadOrderGroup,
        IntPtr lpdwTagId, string? lpDependencies, string? lpServiceStartName,
        string? lpPassword);

    [DllImport(Advapi32, SetLastError = true)]
    public static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport(Advapi32, SetLastError = true)]
    public static extern bool ControlService(IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

    [DllImport(Advapi32, SetLastError = true)]
    public static extern bool QueryServiceStatus(IntPtr hService, ref SERVICE_STATUS lpServiceStatus);

    [DllImport(Advapi32, SetLastError = true)]
    public static extern bool DeleteService(IntPtr hService);

    [DllImport(Advapi32, SetLastError = true)]
    public static extern bool CloseServiceHandle(IntPtr hSCObject);

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    public const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
    public const uint SERVICE_ALL_ACCESS = 0x000F01FF;
    public const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    public const uint SERVICE_AUTO_START = 0x00000002;
    public const uint SERVICE_ERROR_NORMAL = 0x00000001;
    public const uint SERVICE_CONTROL_STOP = 0x00000001;
    public const uint SERVICE_STOPPED = 0x00000001;
    public const uint SERVICE_RUNNING = 0x00000004;
}
