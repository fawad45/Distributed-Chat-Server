using System;
using System.Text;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Collections.Generic;
using Logger;

namespace Test.Server
{
    //using Logger;
    // State object for receiving data from remote device.
    public class StateObject
    {
        // Client socket.
        public Socket workSocket = null;
        // Size of receive buffer.
        public const int BufferSize = 4096;
        // Receive buffer.
        public byte[] buffer = new byte[BufferSize];
        // Received data string.
        public StringBuilder sb = new StringBuilder();
    }

    public class Server {
        private static ManageClients manageClients = new ManageClients();
        private static Dictionary<string, IPEndPoint> dicServersInfo = new Dictionary<string, IPEndPoint>();
        private static HashSet<string> removeClients = new HashSet<string>();
        // The port number for the remote device.
        private const int LoadBalancerport = 8001;

        // ManualResetEvent instances signal completion.
        private static ManualResetEvent connectDone = 
            new ManualResetEvent(false);
        private static ManualResetEvent portSelected =
            new ManualResetEvent(false);
        private static ManualResetEvent sendDone = 
            new ManualResetEvent(false);
        private static ManualResetEvent receiveDone = 
            new ManualResetEvent(false);
        private static Socket loadbalancerTCPsocket;
        private static int ServerPortForClients = 0;
        private static int ServerPortForServers = 0;
        // The response from the remote device.
        private static String response = String.Empty;
        private static bool synchCallReceived = false;
        private static object removeClientLock = new object();

        private static void StartServer() {
            ClientCommunicator._Instance.SetData(manageClients, dicServersInfo);
            
            // Connect to a remote device.
            try {
                
                // Establish the remote endpoint for the socket.
                // The name of the 
                // remote device is "host.contoso.com".
                //IPHostEntry ipHostInfo = Dns.Resolve("host.contoso.com");
                //IPAddress ipAddress = IPAddress.Parse("10.105.27.65"); // ipHostInfo.AddressList[0]; Dev 03
                //IPAddress ipAddress = IPAddress.Parse("10.105.27.127");
                //IPAddress ipAddress = IPAddress.Parse("10.105.27.30");
                IPAddress ipAddress = IPAddress.Parse("10.105.27.28");  // Dev 04
                //IPAddress ipAddress = IPAddress.Parse("10.105.27.27"); // Dev 02

                IPEndPoint remoteEP = new IPEndPoint(ipAddress, LoadBalancerport);

                // Create a TCP/IP socket.
                loadbalancerTCPsocket = new Socket(AddressFamily.InterNetwork,
                    SocketType.Stream, ProtocolType.Tcp);

                // Connect to the remote endpoint.
                loadbalancerTCPsocket.BeginConnect(remoteEP,
                    new AsyncCallback(ConnectCallback), loadbalancerTCPsocket);
                connectDone.WaitOne();

                portSelected.WaitOne();
                // Send test data to the remote device.
                int randValue = new Random().Next(1,99);
                Logger.Logger._Instance.SetFileName("Server-" + randValue);
                Send(loadbalancerTCPsocket, "S-Name:Server-" + randValue + "$" + manageClients.getIPAddressAndPort());

                sendDone.WaitOne();

                var timer = new System.Threading.Timer((e) =>
                {
                    PublishUpdate(loadbalancerTCPsocket);
                }, null, ServerSettings.Default.LoadUpdateTime, ServerSettings.Default.LoadUpdateTime);

                //var timer2 = new System.Threading.Timer((e) =>
                //{
                //    synchDeadClients();
                //}, null, ServerSettings.Default.SynchDeadClients, ServerSettings.Default.SynchDeadClients);
                // Receive the response from the remote device.
                Receive(loadbalancerTCPsocket);
                receiveDone.WaitOne();

                // Write the response to the console.
                //Console.WriteLine("Response received : {0}", response);

                // Release the socket.
                loadbalancerTCPsocket.Shutdown(SocketShutdown.Both);
                loadbalancerTCPsocket.Close();

            } catch (Exception e) {
                Console.WriteLine(e.ToString());
            }
        }

        private static void synchDeadClients()
        {
            if (loadbalancerTCPsocket.Connected)
            {
                List<string> deadClients = manageClients.removeDeadClients();
                try
                {
                    if(deadClients.Count > 0)
                    {
                        StringBuilder sb = new StringBuilder("Remove:");
                        int i = 1;
                        foreach(string deadclient in deadClients)
                        {
                            if(i%100 == 0)
                            {
                                Send(loadbalancerTCPsocket, sb.ToString());
                                sb = new StringBuilder("Remove:");
                            }
                            sb.Append(deadclient + "$");
                            i++;
                        }
                        Send(loadbalancerTCPsocket, sb.ToString());
                    }
                }
                catch (Exception ex)
                {
                    ex.ToString();
                }
            }
            //List<string> loc_removeClients = new List<string>();
           // string[] allClientsKeys = new string[.Count];
            
        }

        private static void ConnectCallback(IAsyncResult ar) {
            try {
                // Retrieve the socket from the state object.
                //Socket client = (Socket) ar.AsyncState;

                // Complete the connection.
                loadbalancerTCPsocket.EndConnect(ar);

               // Console.WriteLine("Socket connected to {0}", loadbalancerTCPsocket.RemoteEndPoint.ToString());

                // Signal that the connection has been made.
                connectDone.Set();
            } catch (Exception e) {
                Console.WriteLine(e.ToString());
                Console.WriteLine("ErrorCode => " + ((SocketException)e).ErrorCode);
            }
        }

        private static void Receive(Socket client) {
            try {
                // Create the state object.
                StateObject state = new StateObject();
                state.workSocket = client;

                // Begin receiving the data from the remote device.
                client.BeginReceive( state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReceiveCallback), state);
            } catch (Exception e) {
                Console.WriteLine(e.ToString());
            }
        }

        private static void ReceiveCallback( IAsyncResult ar ) {
            try {
                // Retrieve the state object and the client socket 
                // from the asynchronous state object.
                StateObject state = (StateObject) ar.AsyncState;
                Socket client = state.workSocket;

                // Read data from the remote device.
                int bytesRead = client.EndReceive(ar);
              //  Console.WriteLine("The Data Receive from LoadBalancer " + bytesRead);
                if (bytesRead > 0) {
                    // There might be more data, so store the data received so far.
                    state.sb.Append(Encoding.ASCII.GetString(state.buffer,0,bytesRead));
                    while (state.sb.Length > 4)
                    {
                        string dataReceive = state.sb.ToString();
                        int sizeOfMessage = System.Convert.ToInt32(dataReceive.Substring(0, 4));
                        if (sizeOfMessage <= state.sb.Length - 4)
                        {
                            response = dataReceive.Substring(4, sizeOfMessage);
                            state.sb = new StringBuilder(dataReceive.Substring(sizeOfMessage + 4));
                            if (response.Contains("End"))
                            {
                                receiveDone.Set();
                                return;
                            }
                            else
                            {
                               // Console.WriteLine(response);
                                //state.sb.Clear();
                                string command = response.Substring(0, 6);
                                response = response.Substring(7);
                                string ReceiveData = response;
                                // The following Commands receive from LoadBalancer
                                if (command.Equals("Self--"))
                                {
                                    // Console.WriteLine("The Receive Data " + ReceiveData);
                                    string[] clientdata = ReceiveData.Split('$');
                                    ClientInfo clientinfo = new ClientInfo(IPAddress.Parse(clientdata[2]), System.Convert.ToInt32(clientdata[3]), clientdata[0], true);
                                    
                                    bool boolresult = manageClients.addClientInfo(clientdata[1], clientinfo, false);
                                    if (boolresult)
                                    {
                                        Send(client, "B-Cast:" + ReceiveData);
                                        //PublishUpdate(client);
                                    }
                                }
                                else if (command.Equals("Other-"))
                                {
                                    try
                                    {
                                        string[] clientdata = ReceiveData.Split('$');
                                        manageClients.addClientInfo(clientdata[1], new ClientInfo(IPAddress.Parse(clientdata[2]), System.Convert.ToInt32(clientdata[3]), clientdata[0]), false);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("The Invalid FormatException => " + response);
                                    }
                                }
                                else if (command.Equals("Remove"))
                                {
                                    lock (removeClientLock)
                                    {
                                        if (synchCallReceived)
                                        {
                                            manageClients.RemoveClientsInfo(ReceiveData);
                                        }
                                        else
                                        {
                                            removeClients.Add(ReceiveData);
                                        }
                                    }
                                }
                                else if (command.Equals("Synch-"))
                                {
                                    synchCallReceive(ReceiveData);
                                }
                                else if (command.Equals("Server"))
                                {
                                    string[] serverdata = ReceiveData.Split('$');
                                    dicServersInfo.Add(serverdata[0], new IPEndPoint(IPAddress.Parse(serverdata[1]), System.Convert.ToInt32(serverdata[2])));
                                }
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                                new AsyncCallback(ReceiveCallback), state);
                }
                else
                {
                    // All the data has arrived; put it in response.
                    //Signal that all bytes have been received.
                    receiveDone.Set();
                }
            } catch (Exception e) {
                Console.WriteLine(e.ToString());
            }
        }

        private static void synchCallReceive(string dataToSynch)
        {
            string[] serversdetails = dataToSynch.Split('&');
            
            for(int i = 0; i < serversdetails.Length; i++)
            {
                if(serversdetails[i] != string.Empty)
                {
                    string[] serverInfodetail = serversdetails[i].Split('#');
                    // 0 Index contains ServerName, 1 contains the IPaddress and Port $ saparated and 2 contains the clients info
                    string serverName = serverInfodetail[0];
                    string[] IPAndPort = serverInfodetail[1].Split('$');
                    dicServersInfo.Add(serverName, new IPEndPoint(IPAddress.Parse(IPAndPort[0]), System.Convert.ToInt32(IPAndPort[1])));
                    if (serverInfodetail[2] != string.Empty)
                    {
                        string[] allConnectedClientsKeys = serverInfodetail[2].Split('$');                        
                        manageClients.AddClientInfoList(allConnectedClientsKeys, serverName, false);
                    }
                   // Console.WriteLine("The Synch Details => " + serverName + " >> " + serverInfodetail[1] + " >> " + serverInfodetail[2]);
                }
            }

            synchCallReceived = true;
            lock (removeClientLock)
            {
                foreach (string removeCallData in removeClients)
                {
                    manageClients.removeClientInfo(removeCallData);
                }
            }
        }

        private static void Send(Socket client, String data) {
            // Convert the string data to byte data using ASCII encoding.
            //string Message = "Update:" + manageClients.getClientsCount();
            string length = ((data.Length) + "").PadLeft(4, '0');
            byte[] byteData = Encoding.ASCII.GetBytes(length + data);
            
            //byte[] byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.
            client.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), client);
        }

        private static void SendCallback(IAsyncResult ar) {
            try {
                // Retrieve the socket from the state object.
                Socket client = (Socket) ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = client.EndSend(ar);
              //  Console.WriteLine("Sent {0} bytes to server.", bytesSent);

                // Signal that all bytes have been sent.
                sendDone.Set();
            } catch (Exception e) {
                Console.WriteLine(e.ToString());
            }
        }

        public static void PublishUpdate(Socket client)
        {
            string Message = "Update:" + manageClients.getClientsCount();
            string length = ((Message.Length) + "").PadLeft(4,'0') ;
            byte[] byteData = Encoding.ASCII.GetBytes( length + Message);
            client.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), client);
            synchDeadClients();
          //  Console.WriteLine("The Connection is going to updatet");
        }
        
        private void connectClient(Object stateInfo)
        {
            IPAddress ipaddress;
            IPAddress.TryParse("127.0.0.1", out ipaddress);
            string[] forClientsPorts = ServerSettings.Default.ForClientsPorts.Split(',');
            ServerPortForClients = System.Convert.ToInt32(forClientsPorts[new Random().Next(0, forClientsPorts.Length - 1)]);
            manageClients.setPort(ServerPortForClients);
            UdpClient listener = new UdpClient(ServerPortForClients);
            IPEndPoint groupEP = new IPEndPoint(ipaddress, ServerPortForClients);
            portSelected.Set();
            while (true)
            {
                byte[] bytes = listener.Receive(ref groupEP);
                string data = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                ClientCommunicator._Instance.enqueueChatMessage(data);
                Logger.Logger._Instance.LogMessage(0, "ClientCommunicatior", "processMessages", " The Receive Data => " + data);
            }
        }

        private void connectServers(Object stateInfo)
        {
          //  Console.WriteLine("The Method connectClient ");
            IPAddress ipaddress;
            IPAddress.TryParse("127.0.0.1", out ipaddress);
            string[] forClientsPorts = ServerSettings.Default.ForServersPorts.Split(',');
            ServerPortForServers = System.Convert.ToInt32(forClientsPorts[new Random().Next(0, forClientsPorts.Length - 1)]);
            manageClients.setPort(ServerPortForClients);
            UdpClient listener = new UdpClient(ServerPortForServers);
            IPEndPoint groupEP = new IPEndPoint(ipaddress, ServerPortForServers);
            while (true)
            {
          //      Console.WriteLine("Waiting for broadcast");
                byte[] bytes = listener.Receive(ref groupEP);
                string data = Encoding.UTF8.GetString(bytes).TrimEnd('\0');

                //ThreadPool.QueueUserWorkItem(new LoadBalancer().SendClientInfoToServer, data);
                // split and get the source and destination from data and take action accordingly
     //           Console.WriteLine("++++++The Message from Client => " + data);
                if (data.Equals("end"))
                {
                    break;
                }
            }
        }
        public static int Main(String[] args)
        {
            ThreadPool.QueueUserWorkItem(new Server().connectClient, 1);
            ThreadPool.QueueUserWorkItem(ClientCommunicator._Instance.processMessages, 2);
            ThreadPool.QueueUserWorkItem(Logger.Logger._Instance.writeToFile, 2);
            
            
            StartServer();
            return 0;
        }
    }
}