using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Net;


namespace Test.LoadBalancerApp
{
    public class StateObject
    {
        // Client  socket.
        public Socket workSocket = null;
        // Size of receive buffer.
        public const int BufferSize = 16384;
        // Receive buffer.
        public byte[] buffer = new byte[BufferSize];
        // Received data string.
        public StringBuilder sb = new StringBuilder(); 
        //public string sb;
        // Server Name Unique
        public string ServerName = null;
    }
    class LoadBalancer
    {
        static ManualResetEvent mre = new ManualResetEvent(false);
        // Thread signal.
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        public static void Main()
        {
            ThreadPool.QueueUserWorkItem(new LoadBalancer().ServerConnect, 0);
            ThreadPool.QueueUserWorkItem(new LoadBalancer().connectClient, 1);
            ThreadPool.SetMinThreads(LoadBalancerSettings.Default.ThreadPoolSize, LoadBalancerSettings.Default.ThreadPoolSize);
            mre.WaitOne();
            Console.WriteLine("Main thread exits.");
        }

        private void ServerConnect(Object stateInfo)
        {
            // Data buffer for incoming data.
            byte[] bytes = new Byte[1024];

            // Establish the local endpoint for the socket.
            // The DNS name of the computer
            // running the listener is "host.contoso.com".
            //IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            //IPAddress ipAddress = IPAddress.Parse("10.105.27.30"); // ipHostInfo.AddressList[0]; //Dev 09
            //IPAddress ipAddress = IPAddress.Parse("10.105.27.27"); // ipHostInfo.AddressList[0]; //Dev 02
            IPAddress ipAddress = IPAddress.Parse("10.105.27.28"); // ipHostInfo.AddressList[0]; //Dev 04

            //IPAddress ipAddress = IPAddress.Parse("10.105.27.127"); // ipHostInfo.AddressList[0]; //Localhost
            
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 8001);

            // Create a TCP/IP socket.
            Socket listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);

            // Bind the socket to the local endpoint and listen for incoming connections.
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    // Set the event to nonsignaled state.
                    allDone.Reset();

                    // Start an asynchronous socket to listen for connections.
                    //Console.WriteLine("Waiting for a connection...");
                    listener.BeginAccept(
                        new AsyncCallback(AcceptCallback),
                        listener);

                    // Wait until a connection is made before continuing.
                    allDone.WaitOne();
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            Console.WriteLine("\nPress ENTER to continue...");
            Console.Read();
        }
        private void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.
            Console.WriteLine("The new Connection is Accept ");
            allDone.Set();

            // Get the socket that handles the client request.
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);

            // Create the state object.
            StateObject state = new StateObject();
            state.workSocket = handler;
            handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                new AsyncCallback(ReadCallback), state);
        }

        private void ReadCallback(IAsyncResult ar)
        {
            String content = String.Empty;

            // Retrieve the state object and the handler socket
            // from the asynchronous state object.
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;
            int bytesRead = -1;
            try
            {
                // Read data from the client socket. 
                bytesRead = handler.EndReceive(ar);
            }
            catch(Exception ex)
            {
                ex.ToString();
                ManageServers._Instance.RemoveServer(state.ServerName);
                state.workSocket.Close();
                return;
            }
            if (bytesRead > 0)
            {
                // There  might be more data, so store the data received so far.
                state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
                //Console.WriteLine(" The Server Message => " + state.sb.ToString());
                // Check for end-of-file tag. If it is not there, read 
                // more data.
                while (state.sb.Length > 4)
                {
                    string dataReceive = state.sb.ToString();
                    int sizeOfMessage = 99999999;
                    try
                    {
                        sizeOfMessage = System.Convert.ToInt32(dataReceive.Substring(0, 4));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("The Exception Occurs => " + state.sb.ToString());
                    }
                    if (sizeOfMessage <= state.sb.Length - 4)
                    {
                        try
                        {
                            content = dataReceive.Substring(4, sizeOfMessage);                            
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(state.sb.ToString());
                            Console.WriteLine(ex.ToString());

                        }
                        state.sb = new StringBuilder(dataReceive.Substring(sizeOfMessage + 4));
                        //content = state.sb;
                        if (content.IndexOf("End") > -1)
                        {
                            // All the data has been read from the 
                            // client. Display it on the console.
                            //Console.WriteLine("Read {0} bytes from socket. \n Data : {1}",
                              //  content.Length, content);
                            ManageServers._Instance.RemoveServer(state.ServerName);
                            handler.Shutdown(SocketShutdown.Both);
                            handler.Close();
                            return;
                        }
                        else
                        {
                            string command = content.Substring(0, 6);
                            content = content.Substring(7);
                            if (command.Equals("S-Name"))
                            {
                                string[] serverdata = content.Split('$');
                                state.ServerName = serverdata[0];
                                ManageServers._Instance.newServer(state.ServerName, new ServerInfo(state.workSocket, 0, serverdata[1], System.Convert.ToInt32(serverdata[2])));
                                Console.WriteLine("The Server Name is => {0}", state.ServerName);
                            }
                            else if (command.Equals("Update"))
                            {
                                int count = System.Convert.ToInt32(content);
                                ManageServers._Instance.UpdateServerclientscount(state.ServerName, count);
                                Console.WriteLine("The Server Name is => {0} and the Client Count {1}", state.ServerName, count);
                            }
                            else if (command.Equals("Remove"))
                            {
                                ManageServers._Instance.synchRemovedClients(content, state.ServerName);
                            }
                            else if (command.Equals("B-Cast"))
                            {
                                ManageServers._Instance.sendClientInfoToAllServer(content);
                            }                            
                        }

                    }
                    else
                    {
                        break;
                    }
                }
                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
            }
        }

        private void connectClient(Object stateInfo)
        {
            IPAddress ipaddress;
            IPAddress.TryParse("127.0.0.1", out ipaddress);
            UdpClient listener = new UdpClient(LoadBalancerSettings.Default.PortForClients);
            IPEndPoint groupEP = new IPEndPoint(ipaddress, LoadBalancerSettings.Default.PortForClients);
            while (true)
            {
                byte[] bytes = listener.Receive(ref groupEP);
                string data = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                ThreadPool.QueueUserWorkItem(new LoadBalancer().SendClientInfoToServer, data);
                if (data.Equals("end"))
                {
                    break;
                }
            }
        }
        
        private void SendClientInfoToServer(Object stateInfo)
        {
            string data = (string)stateInfo;
            broadcastClientInfo(data);
        }

        private void broadcastClientInfo(string data)
        {
            string ServerName = "";
            ServerInfo si = ManageServers._Instance.GetLowestLoadServer(ref ServerName);

            if (si != null)
            {
                //Console.WriteLine("The lowest Load Server =>" + ServerName);
                data = ServerName + "$" + data;
                //Console.WriteLine(data);
                Send(si.getSocket(), "Self--:" + data);
                string[] clientdata = data.Split('$');
                string clientName = clientdata[1];
                si.clientData.addClientInfo(clientName, new Server.ClientInfo(IPAddress.Parse(clientdata[2]), System.Convert.ToInt32(clientdata[3]), clientdata[0], true), true);
            }
            else
            {

                // There is no server but client came for connection. needs to handle this
                return;
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
