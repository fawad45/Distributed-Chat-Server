using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Test.Server;
using System.Threading;

namespace Test.LoadBalancerApp
{
    public class ServerInfo
    {
        public int ConnectedClients;
        Socket tcpSocket;
        string ipaddress;
        int port;
        public ManageClients clientData = new ManageClients();
        public ServerInfo(Socket socket, int count, string ipaddress, int port)
        {
            tcpSocket = socket;
            ConnectedClients = count;
            this.ipaddress = ipaddress;
            this.port = port;
        }

        public Socket getSocket()
        {
            return tcpSocket;
        }

        public int getClientConnectionCount()
        {
            return ConnectedClients;
        }
        public void setClientConnectionCount(int count)
        {
            ConnectedClients = count;
        }
        public void IncClientConnectionCount()
        {
            Interlocked.Increment(ref ConnectedClients);
        }
        public string getAllClientsKeys()
        {
            return clientData.getAllClientsKeys();
        }

        public string getIpaddressAndPort()
        {
            return ipaddress + "$" + port;
        }

        public void removeClient(string clientIDList)
        {
            clientData.RemoveSelfClients(clientIDList);
        }
    }
}
