using KcpSM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    class Program
    {
        static void Main(string[] args)
        {
            KCPUDPSocketServer kcpserver = new KCPUDPSocketServer(new IPEndPoint(IPAddress.Parse("10.2.0.182"), 1525));
            kcpserver.Start();
            Console.ReadLine();
        }
    }
}
