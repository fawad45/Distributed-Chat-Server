using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Net;

namespace Test.Server
{
    public class ManageClients
    {

        Dictionary<string, ClientInfo> dicOthersClient = null;
        Dictionary<string, ClientInfo> dicSelfClient = null;
        //public static ManageClients _Instance = new ManageClients();
        Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        private Object dicselfLock = new Object();
        private Object dicothersLock = new Object();
        private IPAddress ipaddress;
        int connectedClients = 0;
        private int ServerPort = 0;

        List<string> candidatesForRemove = new List<string>();
        object candidatesForRemoveLock = new object();

        public string getAllClientsKeys()
        {
            StringBuilder sb = new StringBuilder();
            string[] allClientKeys;
            lock (dicselfLock)
            {
                allClientKeys = new string[dicSelfClient.Count];
                dicSelfClient.Keys.CopyTo(allClientKeys, 0);
            }
            foreach (string clientKey in allClientKeys)
            {
                sb.Append(clientKey + "$");
            }

            return sb.ToString();
        }

        public void setPort(int port)
        {
            ServerPort = port;
        }

        public ManageClients()
        {
            dicSelfClient = new Dictionary<string, ClientInfo>();
            dicOthersClient = new Dictionary<string, ClientInfo>();
            IPAddress[] ipv4Addresses = Array.FindAll(
                Dns.GetHostEntry(string.Empty).AddressList,
                a => a.AddressFamily == AddressFamily.InterNetwork);
                ipaddress = ipv4Addresses[0];
        }

        public bool addClientInfo(string clientID, ClientInfo clientInfo, bool isLoadBalancer)
        {
            //Console.WriteLine(clientID);
            bool added = false;
            if (clientID != null && clientInfo != null)
            {
                if (clientInfo.isLocalServer)
                {
                    lock (dicselfLock)
                    {
                        if (!dicSelfClient.ContainsKey(clientID))
                        {
                            dicSelfClient[clientID] = clientInfo;
                            Interlocked.Increment(ref connectedClients);
                            added = true;
                        }
                    }
                }
                else
                {
                    lock (dicothersLock)
                    {
                        if (!dicOthersClient.ContainsKey(clientID))
                        {
                            dicOthersClient[clientID] = clientInfo;
                        }
                    }
                }
                if (added && clientInfo.isLocalServer)
                {
                    // The Server is responsible for sending the info to the clients. block the loadbalancer for sending server info
                    if (!isLoadBalancer)
                    {
                        clientInfo.SetServerSocket(s);
                        Logger.Logger._Instance.LogMessage(0, "ManageClients", "addClientInfo", "The ClientID => " + clientID + " Data => " + clientInfo.IpAddress.ToString() + " Port => " + clientInfo.Port);
                        ClientCommunicator._Instance.enqueueChatMessage(0 + "!" + Constant.ServerMessage + clientID + "$" + ipaddress.ToString() + "$" + ServerPort);
                        //IPEndPoint ep = new IPEndPoint(clientInfo.IpAddress, clientInfo.Port);
                        //s.SendTo(Encoding.ASCII.GetBytes(ipaddress.ToString() + "$" + ServerPort), ep);
                    }
                }
            }
            return true;
        }

        public void AddClientInfoList(string[] allclients, string ServerName, bool isloadBalancer)
        { 
            if(!isloadBalancer && allclients.Length > 0)
            {
                lock (dicothersLock)
                {
                    foreach (string clientkey in allclients)
                    {
                        if (clientkey != string.Empty)
                        {
                            dicOthersClient[clientkey] = new ClientInfo(null, 0, ServerName);
                        }
                    }
                }
            }
        }

        public void RemoveClientsInfo(string data)
        {
            //Console.WriteLine("RemoveClientsInfo =>" + data);
            string[] clientsIDs = data.Split('$');
            foreach(string clientID in clientsIDs)
            {
                removeClientInfo(clientID);
            }
        }

        public void removeClientInfo(string clientID)
        {
            ClientInfo ci;
            lock (dicothersLock)
            {
                if (dicOthersClient.TryGetValue(clientID, out ci))
                {
                    dicOthersClient.Remove(clientID);
                }
            }
        }

        public int getClientsCount()
        {
           /// Console.WriteLine("The Total Dictionary {0} The Connected Clients {1}", dicOthersClient.Count, dicSelfClient.Count);
            return dicSelfClient.Count;
        }

        public List<string> removeDeadClients()
        {
            List<string> retval = new List<string>();
            string[] allclientsKeys;
            lock (dicselfLock)
            {
                allclientsKeys = new string[dicSelfClient.Count];
                dicSelfClient.Keys.CopyTo(allclientsKeys, 0);
            }
            foreach (string clientID in allclientsKeys)
            {
                ClientInfo clientinfo;
                if (dicSelfClient.TryGetValue(clientID, out clientinfo))
                {
                    if (clientinfo.lastPingTime.CompareTo((DateTime.Now).AddSeconds((ServerSettings.Default.SessionTimeOutSeconds))) < 0)
                    {
                        Logger.Logger._Instance.LogMessage(0, "ManageClients", "removeDeadClients", "Current time => " + DateTime.Now + "clientinfo.lastPingTime => " + clientinfo.lastPingTime + "Client Name => " + clientID);
                        
                        if (retval.Count > 60)
                        {
                        //    break;
                        }
                        retval.Add(clientID);
                        lock (dicselfLock)
                        {
                            dicSelfClient.Remove(clientID);
                        }                        
                    }
                }
            }
            if (retval.Count > 0)
            {
                Console.WriteLine("The Remove Clients Count due to ping late => " + retval.Count);
            }
            lock (candidatesForRemoveLock)
            {
                foreach (string clientid in candidatesForRemove)
                {
                    if (retval.Count > 100)
                    {
                    //    break;
                    }
                    ClientInfo clientinfo;
                    if (dicSelfClient.TryGetValue(clientid, out clientinfo))
                    {
                        lock (dicselfLock)
                        {
                            dicSelfClient.Remove(clientid);
                        }
                        retval.Add(clientid);
                    }
                }
                
                candidatesForRemove = new List<string>();
            }
            if (retval.Count > 0)
            {
                Console.WriteLine("The Remove Clients Count After Retransmision Fails => " + retval.Count);
            }

            return retval;
        }

        public string getIPAddressAndPort()
        {
            return ipaddress.ToString() + "$" + ServerPort;
        }

        public ClientInfo updateClientPingTime(string ClientID)
        {
           // Console.WriteLine("The Client Name => " + ClientID);
            ClientInfo clientinfo = null;
            lock(dicselfLock)
            {
                dicSelfClient.TryGetValue(ClientID, out clientinfo);
                if (clientinfo != null)
                {
                    clientinfo.updatePingTime();
                }
                else
                {
                    //Console.WriteLine("The Invalid Message => " + ClientID);
                }
            }
            return clientinfo;
        }
        // This Method is used by the loadbalancer
        public void RemoveSelfClients(string clientIDList)
        {
            string[] clientsIDs = clientIDList.Split('$');
            lock (dicselfLock)
            {
                foreach (string clientID in clientsIDs)
                {
                    if (!clientID.Equals(string.Empty))
                    {
                        ClientInfo ci;
                        if (dicSelfClient.TryGetValue(clientID, out ci))
                        {
                            dicSelfClient.Remove(clientID);
                        }
                    }
            
                }
            }
        }

        public string getServerNameAndClientInfo(string clientID, out ClientInfo ci, ref bool isLocalServer)
        {
            string serverName = "";
            lock(dicothersLock)
            {
                if(dicOthersClient.TryGetValue(clientID, out ci))
                {
                    serverName = ci.getServerName();
                    isLocalServer = false;
                }
            }
            if (serverName.Equals(""))
            { 
                lock(dicselfLock)
                {
                    if (dicSelfClient.TryGetValue(clientID, out ci))
                    {
                        serverName = ci.getServerName();
                        isLocalServer = true;
                    }
                }
            }
            return serverName;
        }

        public ClientInfo getClientInfo(string clientName)
        {
            ClientInfo ci;
            lock (dicselfLock)
            {
                if (!dicSelfClient.TryGetValue(clientName, out ci))
                {
                    ci = null;
                }               
            }
            return ci;
        }

        public void RetransmitClientMessages(Socket s)
        {
            //while(true)
            {
                string[] allclientsKeys;
                lock (dicselfLock)
                {
                    allclientsKeys = new string[dicSelfClient.Count];
                    dicSelfClient.Keys.CopyTo(allclientsKeys, 0);
                }

                foreach (string clientID in allclientsKeys)
                {
                    ClientInfo clientinfo;
                    if (dicSelfClient.TryGetValue(clientID, out clientinfo))
                    {
                        try
                        {
                            if (!clientinfo.retransmitMessages(s, false))
                            {
                                lock (candidatesForRemoveLock)
                                {
                                    Logger.Logger._Instance.LogMessage(0, "ManageClients", "RetransmitClientMessages", "Going to Add In Candicate For Remove => " + clientID);
                                    candidatesForRemove.Add(clientID);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                        }
                    }
                }                
            }
        }

        //public void SendMessagestoClient(Socket s)
        //{
        //    //while(true)
        //    {
        //        string[] allclientsKeys;
        //        lock (dicselfLock)
        //        {
        //            allclientsKeys = new string[dicSelfClient.Count];
        //            dicSelfClient.Keys.CopyTo(allclientsKeys, 0);
        //        }

        //        foreach (string clientID in allclientsKeys)
        //        {
        //            ClientInfo clientinfo;
        //            if (dicSelfClient.TryGetValue(clientID, out clientinfo))
        //            {
        //                clientinfo.SendMessage(s);
        //            }
        //        }                
        //    }
        //}
        

        public void ProcessACK(int AckID, string ClientName)
        {
            lock (dicselfLock)
            {
                ClientInfo clientinfo;
                if (dicSelfClient.TryGetValue(ClientName, out clientinfo))
                {
                    clientinfo.ProcessACK(AckID);
                    clientinfo.updatePingTime();
                }
            
            }
        }
    }
}
