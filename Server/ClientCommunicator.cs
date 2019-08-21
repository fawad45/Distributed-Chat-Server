using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
namespace Test.Server
{
    /*
     *  The main responsibilty of that class is to Ack the Clients after collecting the message
     *  send the messages to the respective clients
     *  receive the Ack of all those messages that are deliver to the clients
     * 
     * 
     */
    class ClientCommunicator
    {
        public static ClientCommunicator _Instance = new ClientCommunicator();
        private ManageClients manageclients;
        private static Dictionary<string, IPEndPoint> dicServersInfo;
        private Queue<string> ChatMessages = new Queue<string>();
        private Queue<string> AckSent = new Queue<string>();
        private HashSet<int> AckToReceive = new HashSet<int>();
        private int dequeueCount = 5;
        private object chatMessageLock = new object();
        private static ManualResetEvent ProcessQueue =
            new ManualResetEvent(false);
        Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        int MessageID = 0;
        object MessageIDlock = new object();
        object timer2;
        Object timer3;
        
        private ClientCommunicator()
        {

        }

        public void retransmitClientMessages(Object stateInfo)
        {
           // Console.WriteLine("++++++++++++++ retransmitClientMessages start");
            try
            {
                manageclients.RetransmitClientMessages(s);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
           // Console.WriteLine("++++++++++++++ retransmitClientMessages Ends ");
            
        }

        //public void SendMessagestoClient(Object stateInfo)
        //{
        //    // Console.WriteLine("++++++++++++++ retransmitClientMessages start");
        //    try
        //    {
        //        manageclients.RetransmitClientMessages(s);
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine(ex.ToString());
        //    }
        //    // Console.WriteLine("++++++++++++++ retransmitClientMessages Ends ");

        //}


        private ClientCommunicator(ManageClients mc, Dictionary<string, IPEndPoint> a_dicServersInfo)
        {
            manageclients = mc;
            dicServersInfo = a_dicServersInfo;
            
        }

        public void SetData(ManageClients mc, Dictionary<string, IPEndPoint> a_dicServersInfo)
        {
            manageclients = mc;
            dicServersInfo = a_dicServersInfo;
            timer2 = new System.Threading.Timer((e) =>
            {
                retransmitClientMessages(3);
            }, null, 0, 200);
            
            //timer2 = new System.Threading.Timer((e) =>
            //{
            //    SendMessagestoClient(3);
            //}, null, 0, 500);


        }

        public void setdequeueCount(int count)
        {
            dequeueCount = count;
        }

        public void enqueueChatMessage(string chatmessage)
        {
            lock (chatMessageLock)
            {
                ChatMessages.Enqueue(chatmessage);
            }
            ProcessQueue.Set();            
        }

        public void enqueueAckSent(string chatmessage)
        {
            ChatMessages.Enqueue(chatmessage);
        }

        public void processMessages(Object stateInfo)
        { 
            while(true)
            {
               // Console.WriteLine("The Loop => " + ChatMessages.Count);
                string singleMessage = null;
                lock(chatMessageLock)
                {
                    if (ChatMessages.Count > 0)
                    {
                        singleMessage = ChatMessages.Dequeue();
                   //     Console.WriteLine(" The Message => " + singleMessage);
                    }
                    else
                    {
                        ProcessQueue.Reset();
                    }
                }

                if (singleMessage != null)
                {
                    string[] messagedataarray = singleMessage.Split('!');
                    int messageIDReceived = System.Convert.ToInt32(messagedataarray[0]);
                    string command = messagedataarray[1].Substring(0, 2);
                    string content = messagedataarray[1].Substring(2);
                    singleMessage = messagedataarray[1];
                    if (command.Equals(Constant.ClientPing))
                    {
                        //Console.WriteLine("The Client Ping => " + messageIDReceived);
                        ClientInfo ci = manageclients.updateClientPingTime(content);
                        // Send The ACK to the Respective Client
                        if (ci != null)
                        {
                            Logger.Logger._Instance.LogMessage(0, "ClientCommunicatior", "processMessages", "ACK Sent => " + messageIDReceived + "!" + Constant.ServerACK + content);
                            sendACK(messageIDReceived, ci.ipEP);
                        }
                    }
                    else if (command.Equals(Constant.ClientACK))
                    {
                        Logger.Logger._Instance.LogMessage(0, "ClientCommunicatior", "processMessages", "Client ACK Receive=> " + messageIDReceived + "!" + content);
                        // Console.WriteLine("The Client ACK => " + messageIDReceived);

                        processACK(messageIDReceived, content);
                    }
                    else if (command.Equals(Constant.ClientMessage) || command.Equals(Constant.ClientMessageWithoutACK))
                    {
                        //Console.WriteLine("The message From Client => " + singleMessage);
                        string[] dataReceive = content.Split('$');
                        ClientInfo ci = manageclients.getClientInfo(dataReceive[0]);
                        // Here we Send The ACK of The Received Message
                        if (ci != null && command.Equals(Constant.ClientMessage))
                        {
                            ci.updatePingTime();
                           // Console.WriteLine("ACK Sent From Server " + messageIDReceived);
                            s.SendTo(Encoding.ASCII.GetBytes(messageIDReceived + "!" + Constant.ClientACK), ci.ipEP);
                        }
                         
                        sendMessage(dataReceive);
                    }
                    else if (command.Equals(Constant.ServerMessage))
                    {
                        SendLocalClientMessage(content);
                    }                    
                }
                else
                {
                    ProcessQueue.WaitOne();
                }
            }
        }

        // This method is called just the once in the Client Life. the first message that server send to client

        private void SendLocalClientMessage(string message)
        {
            string[] data = message.Split('$');
            ClientInfo ci = manageclients.getClientInfo(data[0]);
            if(ci != null)
            {
                s.SendTo(Encoding.ASCII.GetBytes(0 + "!" + Constant.ServerMessage + data[1] + "$" + data[2]), ci.ipEP);
            }
        }

        private void sendMessage(string[] dataReceived)
        {
            ClientInfo ci = null;
            bool isLocalServer = false;
            string ServerName = manageclients.getServerNameAndClientInfo(dataReceived[2], out ci, ref isLocalServer);
            IPEndPoint serverEP;
            string Message = Constant.ClientMessage + dataReceived[0] + "$" + dataReceived[1] + "$" + dataReceived[2];
            if (isLocalServer)
            {
                bool response = ci.SendMessage(Message);
                if(!response)
                {

                    //Console.WriteLine(" Fail to Send the MEssage Insert a dummy Message" + Message);
                    enqueueChatMessage(-1 + "!" + Constant.ClientMessageWithoutACK + dataReceived[0] + "$" + dataReceived[1] + "$" + dataReceived[2]);
                }
            }
            else if (!ServerName.Equals("") && dicServersInfo.TryGetValue(ServerName, out serverEP))
            {
                s.SendTo(Encoding.ASCII.GetBytes(-1 + "!" + Constant.ClientMessageWithoutACK + dataReceived[0] + "$" + dataReceived[1] + "$" + dataReceived[2]), serverEP);
            }
            else 
            {
                Logger.Logger._Instance.LogMessage(0, "ClientCommunicatior", "sendMessage(string[] dataReceived)", "ClientName " + dataReceived[2] +  " Client and Server Not Found " + Message);
            }
            // here we insert this message for the ACK receiving
        }

        private void processACK(int AckID, string clientName)
        {
           // Console.WriteLine("The ACK in ClientCommuncation => " + AckID + " Client Name => " + clientName);
            manageclients.ProcessACK(AckID, clientName);
        }

        private void sendACK(int clientmessageID, IPEndPoint ciEP)
        {
            try
            {
                s.SendTo(Encoding.ASCII.GetBytes(clientmessageID + "!" + Constant.ServerACK), ciEP);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
