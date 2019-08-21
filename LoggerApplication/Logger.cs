using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Globalization;
using System.IO;

namespace Logger
{
    public class loggedData
    {
        public int level;
        public string className;
        public string MethodName;
        public string Message;
        public string time;
        public int threadID;

        public string data;

        private void buildData()
        {
            StringBuilder sb = new StringBuilder(threadID + " ");
            sb.Append(time + " ");
            sb.Append(className + " ");
            sb.Append(MethodName);
            sb.Append(" " + Message);
            data = sb.ToString();
        }
        
        override public string ToString()
        {
            //buildData();
            return threadID + " " + time + " " + className + " " + MethodName + " " + Message;
            //return data;
        }
    }

    public class Logger
    {
        private static List<loggedData> MessagesContainer;
        private static List<loggedData> queue1;
        private static List<loggedData> queue2;
        private static int writeQueueid = 1;
        public static Logger _Instance = new Logger();
        private Object QueueLock = new Object();
        SpinLock sl = new SpinLock();
        ManualResetEvent mre = new ManualResetEvent(false);
        private string FileName = "";
        int fileCounter = 0;
        StreamWriter file;
        int counter = 0;

        private Logger()
        {
            //MessagesContainer = new Queue<loggegData>();
            queue1 = new List<loggedData>(1000);
            queue2 = new List<loggedData>(1000);
            MessagesContainer = queue1;
        }

        public void SetFileName(string fileName)
        {
            FileName = fileName;
            file = new StreamWriter(FileName + ".txt", true);
            fileCounter++;            
        }

        public void LogMessage(int level, string ClassName, string Method, string Message)
        {
            int id = Thread.CurrentThread.ManagedThreadId;
            loggedData logeddata = new loggedData() { level = level, className = ClassName, MethodName = Method, Message = Message, threadID = id };
            //logeddata.buildData();
            
            bool gotLock = false;
            try
            {
                sl.Enter(ref gotLock);
               // lock (QueueLock)
                {

                    MessagesContainer.Add(logeddata);
                }
            }
            finally
            {
                if (gotLock)
                {
                    sl.Exit();
                }
            }
            mre.Set();
        }

        public void writeToFile(object obj)
        {
            while (true)
            {
                DateTime dtTimestamp = DateTime.UtcNow;
                string timestamp = dtTimestamp.Hour + ":" + dtTimestamp.Minute + ":" + dtTimestamp.Second + ":" + dtTimestamp.Millisecond;
                List<loggedData> fileWriteQueue;
                bool gotLock = false;

                sl.Enter(ref gotLock);
               // lock (QueueLock)
                try
                {
                    if (writeQueueid == 1)
                    {
                        fileWriteQueue = queue1;
                        writeQueueid = 2;
                        MessagesContainer = queue2;
                    }
                    else
                    {
                        fileWriteQueue = queue2;
                        writeQueueid = 1;
                        MessagesContainer = queue1;
                    }
                    mre.Reset();
                }
                finally 
                {
                    if (gotLock)
                    {
                        sl.Exit();
                    }
                }
                if(counter > 500000)
                {
                    counter = 0;
                    file.Close();
                    file = new StreamWriter((FileName + fileCounter + ".txt"), true);
                    fileCounter++;
                }
                counter += fileWriteQueue.Count;

                if (fileWriteQueue.Count > 0)
                {
                    foreach(loggedData lgData in fileWriteQueue)
                    {
                        //loggedData lgData = fileWriteQueue.();
                        lgData.time = timestamp;
                        string line = lgData.ToString();
                        file.WriteLine(line);
                    }
                    fileWriteQueue.Clear();
                    file.Flush();
                }                
                mre.WaitOne();
            }       
        }

       
    }
}
