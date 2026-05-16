namespace ChildDev.Mobile.Services;

public class ConnectivityService
{
    public virtual bool IsConnected =>
#if ANDROID || IOS || MACCATALYST || WINDOWS
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
#else
        true;
#endif
}
