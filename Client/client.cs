using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Threading;

namespace SATMAP.Client
{

    class UdpState
    {
        public UdpClient u;
        public IPEndPoint e;
    }

    class ExecuteInfo
    { 
        public int startIndex;
        public int count;
        public ManualResetEvent ev;
    }

    class Client
    {
        static ManualResetEvent mre = new ManualResetEvent(false);
        static ManualResetEvent mre2 = new ManualResetEvent(false);
        static string[] stringMessage = {" AAAAAA imersTCP TimersTCP TimersTCP TimersTCP TimersTCP TimersTCP TimersTCP Timers",
                                        " BBBBBB Retransmission TimerRetransmission TimerRetransmission TimerRetransmission Timer "+ 
                                            "Retransmission TimerRetransmission TimerRetransmission TimerRetransmission TimerRetransmission Timer",
                                        " CCCCCCCC Persistence TimerPersistence TimerPersistence TimerPersistence TimerPersistence Timer"+ 
                                            "Persiimple Message Sent to the Client Selected Randomly"+ 
                                            "The Simple Message Sent to the Client Selected Randomly",
                                        " DDDDDDD imple Message Sent to the Client Selected Randomly"+ 
                                            "The Simple Message Sent to the Client Selected Randomly"+ 
                                            "Keep-imple Message Sent to the Client Selected Randomly"+ 
                                            "Keep-imple Message Sent to the Client Selected Randomly"+ 
                                            "The Simple Message Sent to the Client Selected Randomly"};

        Queue<string> SendingMessageBuffer = new Queue<string>();
        object SendingMessageBufferLock = new object();

        Queue<string> ReceivingMessageBuffer;
        object ReceivingMessageBufferLock = new object();
        Queue<string> Queue1 = new Queue<string>();
        Queue<string> Queue2 = new Queue<string>();
        int SelectedQueue = 1;

        public SelfSocket s = null;
        string clientName = "";
        Queue<string> Messages = new Queue<string>();
        object MessagesQueueLock = new Object();
        int MessageFailCount = 0;
        static IPEndPoint ep;
        public Client()
        {
            ReceivingMessageBuffer = Queue1;
        }
        
        public bool connectToLoadBalancer(Object stateInfo)
        {
            s = new SelfSocket(this);
            int clientid = (int)stateInfo;
            clientName = "Client-" + clientid;
            s.setClientName(clientName);
            Logger.Logger._Instance.LogMessage(0, "clients", "connectToLoadBalancer", "Created Client Name => "  +clientName);
            if (s.openUDPPortLocally(startingPortNumber++))
            {
                Logger.Logger._Instance.LogMessage(0, "clients", "connectToLoadBalancer", "Open UDP Port Successfully  ClientName => " + clientName);
                string data = clientName + "$" + s.getIPAndPort();
                //Sending the Client Name to LoadBalancer for getting the available Server

                
                try
                {
                    s.SendData(data, ep);
                }
                catch (Exception ex)
                {
                    Logger.Logger._Instance.LogMessage(0, "clients", "connectToLoadBalancer", "Exception while Sending Info to Loadbalancer  ClientName => " + clientName);
                    Logger.Logger._Instance.LogMessage(0, "clients", "connectToLoadBalancer", " the Data " + data + "  The Exception " + ex.ToString() + " Client Name => " + clientName);
                    ///Console.WriteLine(ex.ToString());
                   /// Console.WriteLine("The Data is => " + data);
                }
                return s.ReceiveData();
            }
            else
            {
                return false;
            }
        }

        

        public bool SendChat(string receiver)
        {
            int time = System.DateTime.Now.Millisecond;
            string Message = time + "  " + stringMessage[new Random(time).Next(0, stringMessage.Length)];
            string sender = clientName;
            lock (SendingMessageBufferLock)
            {
                //if (SendingMessageBuffer.Count < ClientSettings.Default.SendingBufferSize)
                {
                   // Console.WriteLine("test - 2 " + Constant.ClientMessage + sender + "$" + Message + "$" + receiver);
                    //Logger.Logger._Instance.LogMessage(0, "clients", "SendChat", "Sending Message  " + Constant.ClientMessage + sender + "$" + Message + "$" + receiver);
                    string sendingMessage = Constant.ClientMessage + sender + "$" + Message + "$" + receiver;
                    if (!s.SendMessage(sendingMessage))
                    {
                        //Console.WriteLine("The Sliding Window in filled => " + clientName);
                        MessageFailCount++;
                        if (MessageFailCount > 4)
                        {
                            return false;
                        }
                    }
                    //SendingMessageBuffer.Enqueue(Constant.ClientMessage + sender + "$" + Message + "$" + receiver);
                }
                //else
                //{
                 //   MessageFailCount++;
                //}
            }
            return true;
        }

        public List<string> getMessagesToSend(int count)
        {
            List<string> messages = new List<string>();
            lock(SendingMessageBufferLock)
            {
                for(int i = 0; i < SendingMessageBuffer.Count && i < count; i++)
                {
                    messages.Add(SendingMessageBuffer.Dequeue());
                }
            }
            return messages;
        }

        public void ReceiveMessages()
        { 
             s.moveSlidingWindowToBuffer();
        }
        

        public void processMessages()
        {
            Queue<string> copiedBuffer;
            lock(ReceivingMessageBufferLock)
            {
                if (SelectedQueue == 1)
                {
                    copiedBuffer = Queue1;
                    ReceivingMessageBuffer = Queue2;
                    SelectedQueue = 2;
                }
                else
                {
                    copiedBuffer = Queue2;
                    ReceivingMessageBuffer = Queue1;
                    SelectedQueue = 1;
                }                
            }
            int count = copiedBuffer.Count;
            for (int i = 0; i < count; i++ )
            {
                string message = copiedBuffer.Dequeue();
                string command = message.Substring(0, 2);
               // Console.WriteLine("--------------" + message.Substring(2) + "---------------");
                //log message with Respect to the Sender
            }
        }

        public static void methodfortimer(Client[] clientarray, int startIndex, int count)
        {

            //var timeDiff = DateTime.UtcNow - new DateTime(1970, 1, 1);
            //var totaltime = timeDiff.TotalMilliseconds;
            //int clientsPerHundreMilisecond = (Clientscount / timeForMessage) * 100;
            //int threadcount = (Clientscount / timeForMessage);
           // while (true)
          //  {
                ManualResetEvent[] events = new ManualResetEvent[1];//Create a wait handle
               // for (int j = 0; j < threadcount; j++)
               // {
                    events[0] = new ManualResetEvent(false);
                    executeSendMessages(new ExecuteInfo() { ev = events[0], startIndex = startIndex , count = count });
                  //  Thread.Sleep(40);
//}
                //WaitHandle.WaitAll(events);
             //   Thread.Sleep(250);
                
           // }
            //var timeDiffEnd = DateTime.UtcNow - new DateTime(1970, 1, 1);
            //var totaltimeEnd = timeDiffEnd.TotalMilliseconds;

          //  Console.WriteLine("The Time Difference => methodfortimer " + (totaltimeEnd - totaltime));
        }

        public static void executeSendMessages(object info)
        {
            int numberOFSleeps = (timeForMessage/100) - 5;
            int clientsCountBeforeSleep = Math.Max(1, (int)Math.Ceiling((double)(Clientscount / numberOFSleeps)));
            //Console.WriteLine("clientsCountBeforeSleep => " + clientsCountBeforeSleep + "numberOFSleeps " + numberOFSleeps + "timeForMessage " + timeForMessage);
            ExecuteInfo execu = (ExecuteInfo)info;
            for (int i = execu.startIndex; i < execu.startIndex + execu.count && i < Clientscount; i++)
            {
                Logger.Logger._Instance.LogMessage(0, "clients", "executeSendMessages", "Going to Send Message Index => " + i);
                                
                if (clientArray[i] != null)
                {
                    Client client = null;
                    int randomnumber = (new Random(System.DateTime.Now.Millisecond).Next(0, Clientscount));
                    int number = 0;
                    if(randomnumber - i < 0)
                    {
                        number = Clientscount - i;
                    }
                    else
                    {
                        number = randomnumber - i;
                    }
                    client = clientArray[number];
                    if (client != null)
                    {
                        //string clientName = clientArray[(new Random().Next(0, Clientscount))].clientName;
                        //if (clientArray[i] != null && !clientArray[i].SendChat("Client-1"))
                        if (clientArray[i] != null && clientArray[i].s.serverEndPoint != null)
                        {
                            if (!clientArray[i].SendChat(client.clientName))
                            {
                                Logger.Logger._Instance.LogMessage(0, "clients", "executeSendMessages", "Fail to Send Chat  => " + clientArray[i].clientName);
                                clientArray[i] = null;
                            }
                        }
                        else
                        {
                            Logger.Logger._Instance.LogMessage(0, "ABC", "DEF", "The invalid Client Number in Array " + number);
                        }
                    }
                }

                if (i % clientsCountBeforeSleep == 0)
                {
                    Thread.Sleep(100);
                }
            }
            execu.ev.Set();
        }
        

        public static void methodforping(Client[] clientarray, int startIndex, int count)
        {
            //var timeDiff = DateTime.UtcNow - new DateTime(1970, 1, 1);
            //var totaltime = timeDiff.TotalMilliseconds;
            int threadcount = (count / clientsPerThread) + 1;
            ManualResetEvent[] events = new ManualResetEvent[threadcount];//Create a wait handle
            //int sleeptime = 5000 / threadcount;
            //int sleepDuration = 15000/5000
            for (int j = 0; j < threadcount; j++)
            {
                events[j] = new ManualResetEvent(false);
                executePing(new ExecuteInfo() { ev = events[j], startIndex = startIndex + (j * clientsPerThread), count = clientsPerThread });
                Thread.Sleep(150);
            }
            //WaitHandle.WaitAll(events);

            //var timeDiffEnd = DateTime.UtcNow - new DateTime(1970, 1, 1);
            //var totaltimeEnd = timeDiffEnd.TotalMilliseconds;

          //  Console.WriteLine("The Time Difference methodforping => " + (totaltimeEnd - totaltime));

        }

        private static void executePing(object info)
        {
            ExecuteInfo execu = (ExecuteInfo)info;

            for (int i = execu.startIndex; i < execu.startIndex + execu.count && i < Clientscount; i++)
            {
                if (clientArray[i] != null && clientArray[i].s != null)
                {
                    if (!clientArray[i].s.pingServer())
                    {
                        Logger.Logger._Instance.LogMessage(0, "clients", "executePing", "Fail to Send Ping  => " + clientArray[i].clientName);
                            
                        clientArray[i] = null;
                    }
                }
            }
            execu.ev.Set();
        }

        public static void methodReceiveData(Client[] clientarray, int startIndex, int count)
        {
            for (int i = startIndex; i < startIndex + count; i++)
            {
                if (clientarray[i] != null)
                {
                    clientarray[i].ReceiveMessages();
                }
            }
           
        }
        
        private void executeReceiveData(object info)
        {
            ExecuteInfo execu = (ExecuteInfo)info;

            for (int i = execu.startIndex; i < execu.startIndex + execu.count && i < Clientscount; i++)
            {
                if (clientArray[i] != null)
                {
                    clientArray[i].ReceiveMessages();
                    // clientarray[i].processMessages();
                }
            }            
        }

        public static void methodForPendingACK()
        {
 
        }

        public static void methodForSendAndRetransmit(Client[] clientarray, int startIndex, int count)
        {
            for (int i = startIndex; i < startIndex + count; i++)
            {
                if (clientarray[i] != null && clientarray[i].s != null)
                {
                    clientarray[i].s.retransmitMessages();
                }
            }
        }

        static Client[] clientArray;
        static int Clientscount;
        static int startingClientID;
        static int startingPortNumber = 10000;
        static int clientsPerThread = 1000;
        static int MessageRate = 1000;
        static int timeForMessage;
        static void Main(string[] args)
        {
            //Console.WriteLine("Length =>" + args.Length);

            //Console.WriteLine("args[0] =>" + args[0]);
            startingClientID = System.Convert.ToInt32(args[0]);

            startingPortNumber = System.Convert.ToInt32(args[3]);
            Clientscount = System.Convert.ToInt32(args[1]);
            
            Logger.Logger._Instance.SetFileName("Client" + startingClientID);

            IPAddress LoadBAlancerIPAddress = IPAddress.Parse("10.105.27.28");// Dev 04
            //IPAddress LoadBAlancerIPAddress = IPAddress.Parse("10.105.27.27"); // Dev 02
            //IPAddress LoadBAlancerIPAddress = IPAddress.Parse("10.105.27.65");// Dev 03

            //IPAddress LoadBAlancerIPAddress = IPAddress.Parse("10.105.27.127");
            //IPAddress LoadBAlancerIPAddress = IPAddress.Parse("10.111.5.116");
            //IPAddress LoadBAlancerIPAddress = IPAddress.Parse("127.0.0.1");


            ep = new IPEndPoint(LoadBAlancerIPAddress, 8888);
            IPAddress[] ipv4Addresses = Array.FindAll(
            Dns.GetHostEntry(string.Empty).AddressList,
            a => a.AddressFamily == AddressFamily.InterNetwork);
            SelfSocket.ipaddress = ipv4Addresses[0];
            

            clientArray = new Client[Clientscount];
            for (int i = 0; i < Clientscount; i++)
            {
                clientArray[i] = null;
            }
            var timer5 = new System.Threading.Timer((e) =>
            {
                methodforping(clientArray, 0, Clientscount);
            }, null, 0, 5000);

            for (int i = startingClientID; i < startingClientID + Clientscount; i++)
            {
                clientArray[i - startingClientID] = new Client();
                if(!clientArray[i - startingClientID].connectToLoadBalancer(i))
                {
                    clientArray[i - startingClientID] = null;
                   // Console.WriteLine("No Free Port in 1 tries Client Name => Client-" + i);
                }

                if(i%500 == 0)
                {
                    Thread.Sleep(100);
                }
                //System.Threading.Thread.Sleep(100);
            }
            
            System.Threading.Timer timer, timer1, timer2;
            double timeForMessagedouble = Clientscount/MessageRate;
            timeForMessagedouble = Math.Max(1, timeForMessagedouble);
            timeForMessage = (int) Math.Ceiling(timeForMessagedouble) * 1000;
            Console.WriteLine("The Time for Message => " + timeForMessage);
            if (System.Convert.ToInt32(args[2]) == 1)
            {
                //for (int j = 0; j < totalNumberOfBlocks + 1; j++)
                {
                    timer = new System.Threading.Timer((e) =>
                    {
                        //methodfortimer(clientArray, j * totalNumberOfBlocks, clientsPerBlock);
                        methodfortimer(clientArray, 0, Clientscount);
                    }, null, 3000, timeForMessage);

                  //  Console.WriteLine("j => " + j + "j * clientsPerBlock " + j * clientsPerBlock + " j * DifferenceBetweenChatSend " + j * DifferenceBetweenChatSend + " totalNumberOfBlocks * DifferenceBetweenChatSend " + totalNumberOfBlocks * DifferenceBetweenChatSend);
                
                }
                timer1 = new System.Threading.Timer((e) =>
                {
                    methodForSendAndRetransmit(clientArray, 0, Clientscount);
                }, null, 200, 5000);

                timer2 = new System.Threading.Timer((e) =>
                {
                    methodReceiveData(clientArray, 0, Clientscount);
                }, null, 0, 300);

            }
            ThreadPool.QueueUserWorkItem(Logger.Logger._Instance.writeToFile, 4);
            mre.WaitOne();

        }
    }
} 