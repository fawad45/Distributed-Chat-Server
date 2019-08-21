using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;


/*
 * The '!' is use for saparating the Message and Ping ID from Message.
 * Every Message and Ping and ACK must be splited into two parts what saparated using '!'
 * 
 * */
namespace SATMAP.Client
{
    class SelfSocket
    {
        public static int SlidingWindowSize = ClientSettings.Default.SlidingWindowSize;
        UdpClient udpClient;
        IPEndPoint ReceivingEP;
        public static IPAddress ipaddress;
        public IPEndPoint serverEndPoint = null;
        int localPort;
        int MessagesIDSend = 1;
        object MessageIDSendLock = new object();
        string[] lastSaveSendMessages = new string[SlidingWindowSize];
        int[] lastSaveSendMessagesIDs = new int[SlidingWindowSize];
        DateTime[] lastSaveSendMessagesDateTime = new DateTime[SlidingWindowSize];
        int SendIndex = 0;
        int ReceivedACKedID = -1;
        object receivedACKedIDLock = new object();

        int MessagesIDReceive = 0;
        bool ResendACK = false;
        object MessageIDReceiveLock = new object();
        string[] lastSaveReceiveMessages = new string[SlidingWindowSize];
        int[] lastSaveReceiveMessagesIDs = new int[SlidingWindowSize];

        int pingID = 0;
        int FailCount = 0;
        string clientName;
        int LastAckedID = 0;
        Client client;
        bool PingACKReceive = true;
        int PingFailCount = 0;

        public SelfSocket(Client clie)            
        {
            client = clie;
            for (int i = 0; i < SlidingWindowSize; i++)
            {
                lastSaveReceiveMessagesIDs[i] = -1;
                lastSaveSendMessagesIDs[i] = -1;
            }
        }

        public void SendData(string data, IPEndPoint ep)
        { 
            byte[] dataToSend = Encoding.ASCII.GetBytes(data);
            udpClient.Send(dataToSend, dataToSend.Length, ep);

        }

        public void setClientName(string name)
        {
            clientName = name;
        }

        private bool isIDExist(int id, int[] array)
        {
            foreach (int value in array)
            {
                if (value == id)
                    return true;
            }            
            return false;
        }

        public bool openUDPPortLocally(int portNumber)
        {
            int retry = 1;
            bool retVal = false;
            while (retry > 0)
            {
                retry--;
                try
                {
                    localPort = portNumber;// new Random(DateTime.Now.Millisecond).Next(1000, 9999);
                    udpClient = new UdpClient(localPort);
                    ReceivingEP = new IPEndPoint(ipaddress, localPort);
                    retVal = true;
                    break;
                }
                catch (Exception ex)
                {
                    //Console.WriteLine("Socket Already Reserve => " + localPort + "ClientName => " + clientName);
                    //Console.WriteLine(ex.ToString());
                }
            }
            return retVal;
        }

        public bool SendMessage(string Message)
        {
            //Console.WriteLine(" SendMessage(string Message) ");
            bool ReSegmentRequire = false;
            int SendingIndex = SendIndex;
            lock (MessageIDSendLock)
            {
                if((SendIndex >= SlidingWindowSize))
                {
                    ReSegmentRequire = true;
                }
            }
            if (ReSegmentRequire)
            {
                reSegmentMessages();
            }
            lock (MessageIDSendLock)
            {
                SendingIndex = SendIndex;
                if (SendIndex < SlidingWindowSize)
                {
                    lastSaveSendMessages[SendIndex] = Message;
                    lastSaveSendMessagesIDs[SendIndex] = MessagesIDSend;
                    lastSaveSendMessagesDateTime[SendIndex] = DateTime.Now.AddSeconds(2);
                    MessagesIDSend++;
                    SendIndex++;
                }
                else
                {
                    return false;
                }
            }

            sendMessagesToServer(SendingIndex);
            return true;
        }

        public void sendMessagesToServer(int sendstartIndex)
        {
            if (serverEndPoint != null)
            {
                Logger.Logger._Instance.LogMessage(0, "SelfSocket", "sendMessagesToServer(int sendstartIndex)", "Send Data => " + lastSaveSendMessagesIDs[sendstartIndex] + "!" + lastSaveSendMessages[sendstartIndex] + " Client Name " + clientName);
                byte[] dataToSend = Encoding.ASCII.GetBytes(lastSaveSendMessagesIDs[sendstartIndex] + "!" + lastSaveSendMessages[sendstartIndex]);
                udpClient.Send(dataToSend, dataToSend.Length, serverEndPoint);
            }
            
        }

        private void reSegmentMessages()
        {
            lock (receivedACKedIDLock)
            {
                lock (MessageIDSendLock)
                {
                    int i = 0;
                    for (; i < SendIndex && lastSaveSendMessagesIDs[i] <= ReceivedACKedID; i++)
                    {
                        i = ReceivedACKedID - lastSaveSendMessagesIDs[i];
                    }
                    if (i == SendIndex)
                    {
                        for (int ii = 0; ii < SendIndex; ii++)
                        {
                            lastSaveSendMessagesIDs[ii] = -1;
                        }
                        SendIndex = 0;
                    }
                    else if (i > 0)
                    {
                        int j = 0;
                        for (; i < SendIndex; i++, j++)
                        {
                            lastSaveSendMessages[j] = lastSaveSendMessages[i];
                            lastSaveSendMessagesIDs[j] = lastSaveSendMessagesIDs[i];
                        }
                        SendIndex = j;
                    }
                }
            }
        }
        
        public void sendMessages(int sendstartIndex)
        {
            lock (MessageIDSendLock)
            {
                DateTime currentTime = DateTime.Now;
                DateTime nextRetransmitTime = currentTime.AddSeconds(2);
                if (serverEndPoint != null)
                {
                    for (int i = sendstartIndex; i < SendIndex; i++)
                    {
                        if (currentTime.CompareTo(lastSaveSendMessagesDateTime[i]) > 0)
                        {
                            Logger.Logger._Instance.LogMessage(0, "SelfSocket", "sendMessages(int sendstartIndex)", "retransmitMessages Send Data data => " + lastSaveSendMessagesIDs[i] + "!" + lastSaveSendMessages[i] + " Client Name " + clientName);
                            byte[] dataTosend = Encoding.ASCII.GetBytes(lastSaveSendMessagesIDs[i] + "!" + lastSaveSendMessages[i]);
                            udpClient.Send(dataTosend, dataTosend.Length, serverEndPoint);
                            lastSaveSendMessagesDateTime[i] = lastSaveSendMessagesDateTime[i].AddSeconds(2);
                        }
                    }
                }
            }
        }

        public bool retransmitMessages()
        {
            bool SendRequired = false;
            int ResendStartIndex = SendIndex;
            lock (receivedACKedIDLock)
            { 
                lock(MessageIDSendLock)
                {
                    //Console.WriteLine("retransmitMessages 1");
                    int i = 0;
                    for (; i < SendIndex && lastSaveSendMessagesIDs[i] <= ReceivedACKedID; i++ )
                    {
                        i += ReceivedACKedID - lastSaveSendMessagesIDs[i];
                    }
                    if (SendIndex > i)
                    {
                        FailCount++;
                        if (FailCount > ClientSettings.Default.FailCountForDisconnect)
                        {
                            // Needs to Reconnect Again with the Server
                            return false;
                        }
                    }
                    else
                    {
                        FailCount = 0;
                    }
                    if (i == SendIndex)
                    {
                        //Console.WriteLine("retransmitMessages 2");
                        for (int ii = 0; ii < SendIndex; ii++)
                        {
                            //lastSaveReceiveMessagesIDs[i] = -1;
                            lastSaveSendMessagesIDs[ii] = -1;
                        }
                        SendIndex = 0;
                    }
                    else if(i > 0)
                    {
                        ResendStartIndex = i;
                        SendRequired = true;
                    }
                }         
            }
            if (SendRequired)
            {
                Logger.Logger._Instance.LogMessage(0, "SelfSocket", "retransmitMessages", "retransmitMessages data => " + ResendStartIndex +" => " + SendIndex + " Client Name " + clientName);
                sendMessages(ResendStartIndex);
            }
            return true;
        }

        public string getIPAndPort()
        {
            return ipaddress.ToString() + "$" + localPort;
        }

        public bool pingServer()
        {
            //Console.WriteLine("Ping Server");
            if (serverEndPoint != null)
            {
                if (PingACKReceive || PingFailCount <= 5)
                {
                    if (PingACKReceive)
                    {
                        PingFailCount = 0;
                    }
                    else
                    {
                        Logger.Logger._Instance.LogMessage(0, "SelfSocket", "pingServer", "The Ping Response not Receive  => " + (pingID - 1) + " Client Name " + clientName);
                    }
                    string Message;
                    Message = pingID + "!" + Constant.ClientPing + clientName;

                    Logger.Logger._Instance.LogMessage(0, "SelfSocket", "pingServer", "The Data => " + Message + " Client Name " + clientName);
                        
                    pingID++;
                    byte[] dataToSend = Encoding.ASCII.GetBytes(Message);
                    udpClient.Send(dataToSend, dataToSend.Length, serverEndPoint);
                    PingACKReceive = false;
                }
                else
                {                    
                    PingFailCount++;
                    if(PingFailCount > 5)
                    {
                        Logger.Logger._Instance.LogMessage(0, "SelfSocket", "pingServer", "No More Pings  Client Name " + clientName);
                        return false;                        
                    }
                }
            }
            return true;
        }

        private void ReceiveFirstCallback(IAsyncResult ar)
        {
            UdpClient u = (UdpClient)((UdpState)(ar.AsyncState)).u;
            IPEndPoint e = (IPEndPoint)((UdpState)(ar.AsyncState)).e;


            Byte[] receiveBytes;
            try
            {
                receiveBytes = u.EndReceive(ar, ref e); ;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                u.BeginReceive(new AsyncCallback(ReceiveCallback), (UdpState)ar.AsyncState);
                return;
            }
            string ServerMessage = Encoding.ASCII.GetString(receiveBytes);

           // byte[] bytes = udpClient.Receive(ref ReceivingEP);
            //string ServerMessage = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
            //Console.WriteLine("The Server Information =>" + ServerMessage);
            try
            {
                string[] ServerInfo = ServerMessage.Split('!');
                string[] arrserverinfo = ServerInfo[1].Substring(2).Split('$');
                Logger.Logger._Instance.LogMessage(0, "SelfSocket", "ReceiveFirstCallback", "Server Call Receive and going to Connect Client ID => " + clientName);
                serverEndPoint = new IPEndPoint(IPAddress.Parse(arrserverinfo[0]), System.Convert.ToInt32(arrserverinfo[1]));
                Logger.Logger._Instance.LogMessage(0, "SelfSocket", "ReceiveFirstCallback", "Server Call Receive Client ID => " + clientName);
                sendACK(ServerInfo[0]);
                //  Console.WriteLine("The => " + e.Address.ToString() + " AND => " + serverEndPoint.Address.ToString());
                u.BeginReceive(new AsyncCallback(ReceiveCallback), (UdpState)ar.AsyncState);
            }
            catch (Exception ex)
            {
                //Console.WriteLine(ex.ToString());
                //Console.WriteLine("+++++" + ServerMessage + "++++++++");
              //  u.BeginReceive(new AsyncCallback(ReceiveFirstCallback), (UdpState)ar.AsyncState);
            }
            
        }

        
        public bool ReceiveData()
        {
            try
            {
                UdpState udpstate = new UdpState();
                udpstate.u = udpClient;
                udpClient.BeginReceive(new AsyncCallback(ReceiveFirstCallback), udpstate);
                // udpClient.BeginReceive(new AsyncCallback(ReceiveCallback), udpstate);
            
            }catch(Exception ex)
            {
                //Console.WriteLine(ex.ToString());
                return false;
            }
            return true;
        }

        public void sendACK(string messageid)
        {
           // Console.WriteLine("The Sending ACK ID => " + messageid);
            if (serverEndPoint != null){
                byte[] datatoSend = Encoding.ASCII.GetBytes(messageid + "!" + Constant.ClientACK + clientName);
                udpClient.Send(datatoSend, datatoSend.Length, serverEndPoint);
            }
        }

        public void ReceiveCallback(IAsyncResult ar)
        {
            UdpClient u = (UdpClient)((UdpState)(ar.AsyncState)).u;
            IPEndPoint e = (IPEndPoint)((UdpState)(ar.AsyncState)).e;
            string receiveString = "";
            try
            {
                Byte[] receiveBytes = u.EndReceive(ar, ref e);
                receiveString = Encoding.ASCII.GetString(receiveBytes);

                Logger.Logger._Instance.LogMessage(0, "SelfSocket", "ReceiveCallback", "The Receive Message => " + receiveString + "Client Name " + clientName);
                    
                //Console.WriteLine("The => " + e.Address.ToString() + " AND => " + serverEndPoint.Address.ToString());
                //Console.WriteLine("Client Receive Data => " + receiveString);
                if (!e.Address.ToString().Equals(serverEndPoint.Address.ToString()))
                    return;
                bool isDuplicate = false;
                bool isWindowFilled = false;
                int ReceivedataID = 0;

                string[] receivedata = receiveString.Split('!');
                
                ReceivedataID = System.Convert.ToInt32(receivedata[0]);
                string command = receivedata[1].Substring(0, 2);
               
                if (command.Equals(Constant.ClientACK))
                {
                    int receiveACKid = ReceivedataID;// System.Convert.ToInt32(receivedata[1].Substring(2));
                    lock (receivedACKedIDLock)
                    {
                        ReceivedACKedID = Math.Max(ReceivedACKedID, receiveACKid);
                    }
                }
                else if (command.Equals(Constant.ServerACK))
                {
                    PingACKReceive = true;
                }
                else
                {
                    //Console.WriteLine(" --- The Received MEssage => " + receivedata[1].Substring(0,20));
                    int ackid = 0;
                    lock (MessageIDReceiveLock)
                    {
                        ackid = LastAckedID;
                        int index = ReceivedataID - (LastAckedID + 1);
                       // Console.WriteLine("The Received MEssageID is => " + ReceivedataID + " Client Name >>>>>" + clientName + " Last ACKed ID => " + LastAckedID);
                        if (LastAckedID < ReceivedataID && index < SlidingWindowSize)
                        {
                           // Console.WriteLine("The Message Receive  => " + receivedata[1]);
                            //if (!isIDExist(ReceivedataID, lastSaveReceiveMessagesIDs))
                            //{
                                lastSaveReceiveMessages[index] = receivedata[1];
                                lastSaveReceiveMessagesIDs[index] = ReceivedataID;
                                ackid = ReceivedataID;
                                //MessagesIDReceive = Math.Max(MessagesIDReceive, ReceivedataID);
                            //}
                            //else
                            //{
                            //    isDuplicate = true;
                            //    Console.WriteLine("++++ The ID is duplicate of order => " + ReceivedataID + " Last ACKed ID => " + LastAckedID);
                            //}
                        }
                        else
                        {
                            ResendACK = true;
                            //The Packet Is out of order
                            isWindowFilled = true;
                           // Console.WriteLine("++++ The ID is out of order => " + ReceivedataID + " Last ACKed ID => " + LastAckedID);
                        }
                       // ackid = Math.Max(LastAckedID, ReceivedataID);
                       // LastAckedID = ackid;
                    }

                    if (isDuplicate || isWindowFilled)
                    {
                        //sendACK(ackid + "");
                        Logger.Logger._Instance.LogMessage(0, "SelfSocket", "ReceiveCallback", "Window Filled  => " + ReceivedataID + " Last ACKed ID => " + LastAckedID + "Client Name " + clientName);
                       // Console.WriteLine("++++ The ID is out of order => " + ReceivedataID + " Last ACKed ID => " + LastAckedID);
                    }                    
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception occurs " + ex.ToString());
                Console.WriteLine(receiveString);
                Console.WriteLine(e.Port + " " + e.Address.ToString());
            }
            finally
            {
                u.BeginReceive(new AsyncCallback(ReceiveCallback), (UdpState)ar.AsyncState);
            }
        }


        public List<string> moveSlidingWindowToBuffer()
        {
            List<string> ReceiveMessages = null;
            int idForAck = -1;
            //bool anyNewMessage = false;
            int LastAck = 0;
            lock(MessageIDReceiveLock)
            {
                idForAck = LastAckedID;
                LastAck = LastAckedID;
                int i = 0;
                for (; i < SlidingWindowSize && lastSaveReceiveMessagesIDs[i] != -1; i++)
                {
                   // ReceiveMessages.Add(lastSaveReceiveMessages[i]);
                    idForAck = lastSaveReceiveMessagesIDs[i];
                    lastSaveReceiveMessagesIDs[i] = -1;
                   // lastSaveReceiveMessages[i] = "";
                   // anyNewMessage = true;
                }
               // LastAckedID = Math.Max(LastAckedID, idForAck);
               // idForAck = LastAckedID;
            }
           // Console.WriteLine("idForAck > LastAckedID" + idForAck  + " > " + LastAckedID);
            if (idForAck > LastAck || ResendACK)
            {
                LastAckedID = idForAck;
                sendACK(idForAck + "");
                Logger.Logger._Instance.LogMessage(0, "SelfSocket", "moveSlidingWindowToBuffer", "Sending ACK  => " + LastAckedID + " Client Name " + clientName);
                ResendACK = false;
               // Console.WriteLine("Sending ACK => " + idForAck);
            }
            return ReceiveMessages;
        }
    }
}
