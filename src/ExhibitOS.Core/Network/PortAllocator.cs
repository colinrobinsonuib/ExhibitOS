using System.Net;
using System.Net.Sockets;

namespace ExhibitOS.Core.Network;

public static class PortAllocator
{
    public static int GetAvailableLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
