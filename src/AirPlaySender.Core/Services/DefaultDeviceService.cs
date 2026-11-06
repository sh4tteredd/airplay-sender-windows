using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace AirPlaySender.Core.Services;

/// <summary>
/// Changes the Windows default playback endpoint via the undocumented
/// IPolicyConfig COM interface (the same mechanism used by audio switchers).
/// </summary>
public sealed class DefaultDeviceService
{
    public string? GetDefaultRenderId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.ID;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Could not read the default render endpoint", ex);
            return null;
        }
    }

    public bool SetDefaultRender(string endpointId)
    {
        if (string.IsNullOrWhiteSpace(endpointId)) return false;

        IPolicyConfig? policy = null;
        try
        {
            policy = (IPolicyConfig)new CPolicyConfigClient();
            var first = 0;
            for (var role = 0; role <= 2; role++)
            {
                var hr = policy.SetDefaultEndpoint(endpointId, role);
                if (role == 0) first = hr;
                if (hr != 0) Logger.Instance.Warn($"SetDefaultEndpoint(role={role}) returned 0x{hr:X8}");
            }
            if (first != 0) Marshal.ThrowExceptionForHR(first);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Could not change the default playback endpoint", ex);
            return false;
        }
        finally
        {
            if (policy is not null) Marshal.ReleaseComObject(policy);
        }
    }

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class CPolicyConfigClient
    {
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string pszDeviceName, out IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat(string pszDeviceName, bool bDefault, out IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
        [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode(string pszDeviceName, out IntPtr pMode);
        [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string pszDeviceName, IntPtr key, out IntPtr pv);
        [PreserveSig] int SetPropertyValue(string pszDeviceName, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint(string pszDeviceName, int role);
        [PreserveSig] int SetEndpointVisibility(string pszDeviceName, bool visible);
    }
}
