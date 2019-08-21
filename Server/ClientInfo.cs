using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace Test.Server
{
    public class ClientInfo
    {
        public IPAddress IpAddress;
        public int Port;
        public bool isLocalServer;
        public DateTime lastPingTime;
        string serverName;
        public IPEndPoint ipEP = null;
        int lastSentMessageIndex = 0;
        int MessageID = 1;
        public static int SlidingWindowSize = ServerSettings.Default.SlidingWindowSize;
        string[] LastSendMessages = new string[SlidingWindowSize];
        int[] LastSendMessagesIDs = new int[SlidingWindowSize];
        DateTime[] LastSendMessagesRetransmitionTime = new DateTime[SlidingWindowSize];
        
        object LastSendMessagesLock = new object();
        int SendIndex = 0;

        int lastAckedReceivedID = -1;
        object lastAckedReceivedIDLock = new object();
        int FailCount = 0;

        Socket ServerSocket = null;

        public DateTime RetransmitTime;
        

        public ClientInfo(IPAddress ipaddress, int port, string serverName, bool localServer = false)
        {
            IpAddress = ipaddress;
            Port = port;
            isLocalServer = localServer;
            lastPingTime = DateTime.Now;
            this.serverName = serverName;
            if(ipaddress != null)
                ipEP = new IPEndPoint(ipaddress, port);
        }

        public void SetServerSocket(Socket s)
        {
            ServerSocket = s;
        }

        public void updatePingTime()
        {
            lastPingTime = DateTime.Now;
            //Console.WriteLine("The New Ping Time Of Client => " + lastPingTime.ToString());
        }

        public string getServerName()
        {
            return serverName;
        }

        public void ProcessACK(int ACKID)
        {
          //  Console.WriteLine("The Process ACK ID => " + ACKID + " The Client Port  => " + Port);
            lock (lastAckedReceivedIDLock)
            {
                lastAckedReceivedID = Math.Max(lastAckedReceivedID, ACKID);
            }
        }

        public bool SendMessage(string Message)
        {
            bool retVal = false;
            lock (LastSendMessagesLock)
            {
                if (SlidingWindowSize > SendIndex)
                {
                    LastSendMessages[SendIndex] = Message;
                    LastSendMessagesIDs[SendIndex] = MessageID;
                    Logger.Logger._Instance.LogMessage(0, "ClientInfo", "SendMessage", "Send Message  The Index is => " + SendIndex);
                    retVal = SendMessage(ServerSocket, SendIndex);
                    MessageID++;
                    SendIndex++;                    
                }                
            }
            if (!retVal)
            {
               retransmitMessages(ServerSocket, false);
            }
            return retVal;
        }

        private bool SendMessage(Socket s, int index)
        {
            // Console.WriteLine("++++ Going To Send Message => " + LastSendMessagesIDs[index] + "!" + LastSendMessages[index]);
            try
            {
                Logger.Logger._Instance.LogMessage(0, "ClientInfo", "SendMessage", "Send Message  => " + LastSendMessagesIDs[index] + "!" + LastSendMessages[index]);

                LastSendMessagesRetransmitionTime[index] = (DateTime.Now.AddSeconds(3));
                byte[] dataToSend = Encoding.ASCII.GetBytes(LastSendMessagesIDs[index] + "!" + LastSendMessages[index]);
                if (ipEP != null)
                {
                    s.SendTo(dataToSend, ipEP);
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(" Excepton While Sending Data");
                Logger.Logger._Instance.LogMessage(0, "ClientInfo", "SendMessage", "Exception => " + ex.ToString() );
                return false;
            }
            return true;
        }

        public void SendMessage(Socket s)
        {
            DateTime currentTime = DateTime.Now;
            int counter = 0;
            for (int i = 0; i < SendIndex && lastAckedReceivedID > -1; i++)
            {
                if (lastAckedReceivedID < LastSendMessagesIDs[i] && currentTime.CompareTo(LastSendMessagesRetransmitionTime[i]) > 0)
                {
                    try
                    {

                        Logger.Logger._Instance.LogMessage(0, "ClientInfo", "SendMessage", "Retransmit message  => " + (LastSendMessagesIDs[i] + "!" + LastSendMessages[i]) + " The LastACKReceivedID " + lastAckedReceivedID + " SendingIndex " + SendIndex);
                        Logger.Logger._Instance.LogMessage(0, "ClientInfo", "SendMessage", "Retransmit Time  => " + currentTime.ToString() + " The Message Send Time => " + LastSendMessagesRetransmitionTime[i].ToString());
                        if (ipEP != null)
                        {
                            s.SendTo(Encoding.ASCII.GetBytes(LastSendMessagesIDs[i] + "!" + LastSendMessages[i]), ipEP);
                        }
                        LastSendMessagesRetransmitionTime[i] = LastSendMessagesRetransmitionTime[i].AddSeconds(3);
                        counter++;
                        if (counter%10 == 0)
                        {
                            LastSendMessagesRetransmitionTime[i].AddMilliseconds(200);
                        }
                    }
                    catch (Exception ex)
                    { 
                        
                    }
                }
            }
        }



        public bool retransmitMessages(Socket s, bool iswithoutSend)
        {
           // Console.WriteLine("The retransmitMessages is called 1");
           // lock (lastAckedReceivedIDLock)
            {
                lock (LastSendMessagesLock)
                {
                    int i = 0;
                  //  Console.WriteLine("The retransmitMessages is called 1.0 +" + lastAckedReceivedID);
                    for (; i < SendIndex && LastSendMessagesIDs[i] <= lastAckedReceivedID; i++)
                    {
                        i += lastAckedReceivedID - LastSendMessagesIDs[i];
                    }
                 //   Console.WriteLine("The retransmitMessages is called 2.1 " + i + "  " + SendIndex);

                    if (SendIndex > i && (DateTime.Now.CompareTo(LastSendMessagesRetransmitionTime[i]) > 0))
                    {
                    //    Console.WriteLine("The retransmitMessages is called 3");
                        FailCount++;
                        if (FailCount > ServerSettings.Default.FailCountForDisconnect)
                        {
                            Logger.Logger._Instance.LogMessage(0, "ClientInfo", "retransmitMessages", "Retransmit message  Count increase => " + LastSendMessagesRetransmitionTime[i].ToString() + " Current Time " + DateTime.Now.ToString());
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
                        //LastSendMessages = new string[ServerSettings.Default.SlidingWindowSize];
                        LastSendMessagesIDs = new int[SlidingWindowSize];
                        SendIndex = 0;
                        lastSentMessageIndex = 0;
                    }
                    else if (i > 0)
                    {
                        int j = 0;
                        for (; i < SendIndex; i++, j++)
                        {
                            LastSendMessages[j] = LastSendMessages[i];
                            LastSendMessagesIDs[j] = LastSendMessagesIDs[i];
                            LastSendMessagesRetransmitionTime[j] = LastSendMessagesRetransmitionTime[i];
                        }
                        SendIndex = j;
                        lastSentMessageIndex = j;
                    }
                }
            }
            if (!iswithoutSend)
            {
                SendMessage(s);
            }
            return true;
        }
    }
}
