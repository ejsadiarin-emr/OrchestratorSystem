using System.ComponentModel;
using System.Runtime.InteropServices;
using Agent.PInvoke;

namespace Agent.Services;

public sealed class ScmService : IDisposable
{
    private IntPtr _hSCManager;
    private bool _disposed;

    private void EnsureScmOpen()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ScmService));

        if (_hSCManager == IntPtr.Zero)
        {
            _hSCManager = ScmNativeMethods.OpenSCManager(null, null, ScmNativeMethods.SC_MANAGER_CREATE_SERVICE);
            if (_hSCManager == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open Service Control Manager.");
        }
    }

    public Task InstallServiceAsync(string serviceName, string binaryPath)
    {
        return Task.Run(() =>
        {
            EnsureScmOpen();

            var hService = ScmNativeMethods.CreateService(
                _hSCManager,
                serviceName,
                serviceName,
                ScmNativeMethods.SERVICE_ALL_ACCESS,
                ScmNativeMethods.SERVICE_WIN32_OWN_PROCESS,
                ScmNativeMethods.SERVICE_AUTO_START,
                ScmNativeMethods.SERVICE_ERROR_NORMAL,
                binaryPath,
                null,
                IntPtr.Zero,
                null,
                null,
                null);

            if (hService == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1073) // ERROR_SERVICE_EXISTS
                    return;
                throw new Win32Exception(error, $"Failed to create service '{serviceName}'.");
            }

            ScmNativeMethods.CloseServiceHandle(hService);
        });
    }

    public Task StartServiceAsync(string serviceName)
    {
        return Task.Run(() =>
        {
            EnsureScmOpen();

            var hService = ScmNativeMethods.OpenServiceW(_hSCManager, serviceName, ScmNativeMethods.SERVICE_ALL_ACCESS);
            if (hService == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to open service '{serviceName}'.");

            try
            {
                if (!ScmNativeMethods.StartService(hService, 0, IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 1056) // ERROR_SERVICE_ALREADY_RUNNING
                        return;
                    throw new Win32Exception(error, $"Failed to start service '{serviceName}'.");
                }
            }
            finally
            {
                ScmNativeMethods.CloseServiceHandle(hService);
            }
        });
    }

    public Task StopServiceAsync(string serviceName)
    {
        return Task.Run(() =>
        {
            EnsureScmOpen();

            var hService = ScmNativeMethods.OpenServiceW(_hSCManager, serviceName, ScmNativeMethods.SERVICE_ALL_ACCESS);
            if (hService == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1060) // ERROR_SERVICE_DOES_NOT_EXIST
                    return;
                throw new Win32Exception(error, $"Failed to open service '{serviceName}'.");
            }

            try
            {
                var status = new ScmNativeMethods.SERVICE_STATUS();
                if (!ScmNativeMethods.ControlService(hService, ScmNativeMethods.SERVICE_CONTROL_STOP, ref status))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 1062) // ERROR_SERVICE_NOT_ACTIVE
                        return;
                    throw new Win32Exception(error, $"Failed to stop service '{serviceName}'.");
                }
            }
            finally
            {
                ScmNativeMethods.CloseServiceHandle(hService);
            }
        });
    }

    public Task DeleteServiceAsync(string serviceName)
    {
        return Task.Run(() =>
        {
            EnsureScmOpen();

            var hService = ScmNativeMethods.OpenServiceW(_hSCManager, serviceName, ScmNativeMethods.SERVICE_ALL_ACCESS);
            if (hService == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1060) // ERROR_SERVICE_DOES_NOT_EXIST
                    return;
                throw new Win32Exception(error, $"Failed to open service '{serviceName}'.");
            }

            try
            {
                if (!ScmNativeMethods.DeleteService(hService))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 1072) // ERROR_SERVICE_MARKED_FOR_DELETE
                        return;
                    throw new Win32Exception(error, $"Failed to delete service '{serviceName}'.");
                }
            }
            finally
            {
                ScmNativeMethods.CloseServiceHandle(hService);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_hSCManager != IntPtr.Zero)
        {
            ScmNativeMethods.CloseServiceHandle(_hSCManager);
            _hSCManager = IntPtr.Zero;
        }

        _disposed = true;
    }
}
