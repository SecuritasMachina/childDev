namespace ChildDev.Mobile.Services;

public class ConnectivityService
{
    public virtual bool IsConnected =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
}
