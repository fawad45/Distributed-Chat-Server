using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;

namespace Test.LoadBalancerApp
{
    class ManageServers
    {
        private Dictionary<string, ServerInfo> dic_servers;
        public static ManageServers _Instance = new ManageServers();
        private ServerInfo cachedServer;
        private int RemainingServeCount = 0;
        private Object DicLock = new Object();
        private Object cacheLock = new Object();
        private string cachedServerName;
        private ManageServers()
        {
            dic_servers = new Dictionary<string, ServerInfo>();
        }

        public void newServer(string name, ServerInfo info)
        {
            Console.WriteLine(" **newServer** The Server Count is => {0}", dic_servers.Count);
            if (name != null && info != null)
            {
                // Lock Implementation
                lock (DicLock)
                {
                    dic_servers[name] = info;
                }
                Send(info.getSocket(), InfoForSynch(name));
                synchServerinfo(name + "$" + info.getIpaddressAndPort(), name);
            }
        }

        private string InfoForSynch(string servername)
        {
            StringBuilder sb = new StringBuilder("Synch-:");
            string[] allServerKeys;
            lock (DicLock)
            {
                allServerKeys = new string[dic_servers.Count];
                dic_servers.Keys.CopyTo(allServerKeys, 0);
            }
            foreach (string serverKey in allServerKeys)
            {
                ServerInfo server;
                if (servername != serverKey && dic_servers.TryGetValue(serverKey, out server))
                {
                    sb.Append(serverKey + "#");
                    sb.Append(server.getIpaddressAndPort() +"#");
                    sb.Append(server.getAllClientsKeys() + "&");
                }
            }
            return sb.ToString();
        }

        private void synchServerinfo(string Serverdata, string serverName)
        {
            string[] allServerKeys;
            lock (DicLock)
            {
                allServerKeys = new string[dic_servers.Count];
                dic_servers.Keys.CopyTo(allServerKeys, 0);
            }
            foreach (string serverKey in allServerKeys)
            {
                if (serverKey != serverName)
                {
                    ServerInfo server;
                    if (dic_servers.TryGetValue(serverKey, out server))
                    {
                        Send(server.getSocket(), "Server:" + Serverdata);
                    }
                }
            }
        }

        public void UpdateServerclientscount(string name, int count)
        {
            lock (DicLock)
            {
                if (name != null && dic_servers.ContainsKey(name))
                    dic_servers[name].setClientConnectionCount(count);
            }
        }

        public void RemoveServer(string name)
        {
            Console.WriteLine(" **RemoveServer** The Server Count is => {0}", dic_servers.Count);
            if (name != null)
            {
                // use Lock here
                lock(cacheLock)
                {
                    if (name == cachedServerName)
                    {
                        RemainingServeCount = 0;
                    }
                }
                lock (DicLock)
                {
                    dic_servers.Remove(name);
                }
            }
        }

        public ServerInfo GetLowestLoadServer(ref string ServerName)
        {
            //Console.WriteLine("The Value of Count => " + RemainingServeCount);
            lock (cacheLock)
            {
                if (RemainingServeCount > 0)
                {
                    Interlocked.Decrement(ref RemainingServeCount);
                    ServerName = cachedServerName;
                    return cachedServer;
                }
                ServerInfo minCountServer = new ServerInfo(null, 9999999, "",  9999999);
                lock (DicLock)
                {
                    foreach (string serverKey in dic_servers.Keys)
                    {
                        ServerInfo server = dic_servers[serverKey];
                        if (minCountServer.ConnectedClients > server.ConnectedClients)
                        {
                            minCountServer = server;
                            ServerName = serverKey;
                        }
                    }
                }
                cachedServer = minCountServer;
                cachedServerName = ServerName;
                RemainingServeCount = LoadBalancerSettings.Default.ConsecutiveClientConnection;
                return minCountServer;
            }            
        }

        public void sendClientInfoToAllServer(string data)
        {
            string[] allServerKeys;
            string serverName = (data.Split('$'))[0];
            lock (DicLock)
            {
                allServerKeys = new string[dic_servers.Count];
                dic_servers.Keys.CopyTo(allServerKeys, 0);
                dic_servers[serverName].IncClientConnectionCount();
            }
            foreach (string serverKey in allServerKeys)
            {
                ServerInfo server;
                if (dic_servers.TryGetValue(serverKey, out server))
                {
                    if (!serverName.Equals(serverKey))
                    {
                        Send(server.getSocket(), "Other-:" + data);
                    }
                    else 
                    {
                        server.IncClientConnectionCount();
                    }
                }
                
            }
        }

        public void synchRemovedClients(string data, string sendingServer)
        {
            //Console.WriteLine("synchRemovedClients => " + data);
            string[] allServerKeys;
            lock (DicLock)
            {
                allServerKeys = new string[dic_servers.Count];
                dic_servers.Keys.CopyTo(allServerKeys, 0);
            }
            foreach (string serverKey in allServerKeys)
            {
                
                ServerInfo server;
                if (dic_servers.TryGetValue(serverKey, out server))
                {
                    if (!serverKey.Equals(sendingServer))
                    {
                        Send(server.getSocket(), "Remove:" + data);
                    }
                    else
                    {
                        Console.WriteLine("The ServerName => " + serverKey + "The Connected Clients before Remove => " + server.clientData.getClientsCount());
                        server.removeClient(data);
                        Console.WriteLine("The ServerName => " + serverKey + "The Connected Clients After Remove => " + server.clientData.getClientsCount());                        
                    }
                }                
            }
        }

        private void Send(Socket handler, String data)
        {
            // Convert the string data to byte data using ASCII encoding.
            string length = ((data.Length) + "").PadLeft(4, '0');
            byte[] byteData = Encoding.ASCII.GetBytes(length + data);
            
            //byte[] byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.
            handler.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), handler);
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket handler = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = handler.EndSend(ar);
               // Console.WriteLine("Sent {0} bytes to client.", bytesSent);

                //handler.Shutdown(SocketShutdown.Both);
                //handler.Close();

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

    }
}
