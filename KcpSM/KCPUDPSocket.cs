using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSM
{
    public class KCPUDPSocketServer
    {
        IPEndPoint listener = null;
        Socket socket = null;
        byte[] buffer = new byte[65535];
        public KCPUDPSocketServer(IPEndPoint point)
        {
            this.listener = point;
        }
        public void Start()
        {
            this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            EndPoint ipendpoint = new IPEndPoint(IPAddress.Loopback, 0);
            this.socket.Bind(listener);
            this.socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref ipendpoint, EndRecv, null);
        }
        private void EndRecv(IAsyncResult result)
        {
            EndPoint remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 0);
            try
            {
                int recvDataLength = this.socket.EndReceiveFrom(result, ref remoteEndpoint);
                var recvData = this.buffer.Take(recvDataLength).ToArray();
                Check(recvData);
            }
            catch
            {

            }
            finally
            {
                this.socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remoteEndpoint, EndRecv, null);
            }
        }
        Dictionary<UInt32, Tuple<Queue<KCPPackage>, KCP>> SessionDic = new Dictionary<uint, Tuple<Queue<KCPPackage>, KCP>>();
        private void Check(byte[] data)
        {
            int index = 0;
            var conv = ConverHelper.GetU32(data, index);
            Tuple<Queue<KCPPackage>, KCP> item = null;
            lock (SessionDic)
            {
                if (!SessionDic.TryGetValue(conv, out item))
                {
                    item = new Tuple<Queue<KCPPackage>, KCP>(new Queue<KCPPackage>(), new KCP(conv));
                    //如果会话中不存在conv则认为这是新连接,创建KCP状态机
                    SessionDic.Add(conv, item);
                    new Thread(Update) { IsBackground = true }.Start(item);
                }
            }

            while (index < data.Length)
            {
                var tempItem = KCPPackage.Parse(data, index);
                if (tempItem.Item2 == 0)
                    break;
                index += tempItem.Item2;
                item.Item1.Enqueue(tempItem.Item1);
            }
        }
        private void Update(Object o)
        {
            var item = o as Tuple<Queue<KCPPackage>, KCP>;
            while(true)
            {
                //input + peek + get
                while(item.Item1.Count > 0)
                {
                    var t = item.Item1.Dequeue();
                    item.Item2.input(t);
                }
                for(int i = item.Item2.PeekCount(); i > 0; i = item.Item2.PeekCount())
                {
                    var data = new byte[i];
                    item.Item2.GetMsg(data);
                    Console.WriteLine(Encoding.ASCII.GetString(data));
                }
                Thread.Sleep(20);
            }
        }
    }

    public class KCP
    {
        private UInt32 _conv;
        private KCPPackage[] recv_buff = new KCPPackage[0];
        private KCPPackage[] recv_queue = new KCPPackage[0];
        private uint remoteRecvWnd = 32; //默认初始远端wnd值
        private KCPPackage[] send_queue = new KCPPackage[0];
        private KCPPackage[] send_buf = new KCPPackage[0];
        private int rcv_next = 0;
        private int rcv_wnd = 32;

        private Tuple<UInt32, UInt32>[] acklist = new Tuple<UInt32, UInt32>[0];
        public KCP(UInt32 conv)
        {
            this._conv = conv;
        }
        public int input(KCPPackage data)
        {
            if (data.Conv != this._conv)
                return -1;
            if (data.Cmd < 81 || data.Cmd > 84)
                return -2;
            remoteRecvWnd = data.Wnd;
            int needAckedCount = 0;
            for (int i = 0; i < this.send_buf.Length; i++)
                if (data.Una > send_buf[i].Sn)
                    needAckedCount++;
                else
                    break;
            if (needAckedCount > 0)
                this.send_buf = slice<KCPPackage>(this.send_buf, needAckedCount, this.send_buf.Length);
            if(data.Cmd == 82)//ack
            {
                if (this.send_buf.Length > 0)
                {
                    if (this.send_buf[0].Sn <= data.Sn)
                    {
                        int index = 0;
                        foreach (var pack in this.send_buf)
                        {
                            if (pack.Sn == data.Sn)
                            {
                                this.send_buf = append<KCPPackage>(slice<KCPPackage>(this.send_buf, 0, index), slice<KCPPackage>(this.send_buf, index + 1, send_buf.Length));
                                break;
                            }
                            else
                            {
                                pack.fastack++;
                            }
                            index++;
                        }
                    }
                }
            }
            if(data.Cmd == 81)//push
            {
                if (this.rcv_next + this.rcv_wnd > data.Sn)
                {
                    //添加到待回执列表
                    append<Tuple<UInt32, UInt32>>(this.acklist, new Tuple<UInt32, UInt32>(data.Sn, ))
                    bool exist = false;
                    int topper = -1;
                    if (this.rcv_next <= data.Sn)
                    {
                        for(int i = 0; i < this.recv_buff.Length; i++)
                        {
                            var pack = this.recv_buff[i];
                            if(pack.Sn == data.Sn)
                            {
                                exist = true;
                                break;
                            }
                            if (data.Sn > pack.Sn)
                            {
                                topper = i;
                                break;
                            }
                        }
                    }
                    if(!exist)
                    {
                        if(topper == -1)
                        {
                            recv_buff = append<KCPPackage>(recv_buff, data);
                        }
                        else
                        {
                            recv_buff = append<KCPPackage>(slice<KCPPackage>(recv_buff, 0, topper), append<KCPPackage>(new KCPPackage[] { data }, slice<KCPPackage>(recv_buff, topper + 1, recv_buff.Length)));
                        }
                    }
                    int removecount = 0;
                    foreach(var pack in this.recv_buff)
                    {
                        if(this.rcv_next == pack.Sn)
                        {
                            this.recv_queue = append<KCPPackage>(this.recv_queue, pack);
                            this.rcv_next++;
                            removecount++;
                        }
                    }
                    if(removecount > 0)
                    {
                        this.recv_buff = slice<KCPPackage>(this.recv_buff, removecount, this.recv_buff.Length);
                    }
                }
            }
            if (data.Cmd == 83)
            {
                //todo wnd ask
            }
            if (data.Cmd == 84)
            {
                
            }
            return 0;
        }
        public int PeekCount()
        {
            int length = 0;
            if (this.recv_queue.Length == 0)
                length = -1;
            else if (this.recv_queue[0].Frg == 0)
                length = this.recv_queue[0].Data.Length;
            else if (this.recv_queue[0].Frg < this.recv_queue.Length)
                for(int i = this.recv_queue[0].Frg + 1; i > 0; i++)
                    length += recv_queue[i].Data.Length;
            else
                length = -1;
            return length;
        }
        public bool GetMsg(byte[] data)
        {
            //不做校验了,假定一定是Peek之后调用GetMsg()
            int startIndex = 0;
            int packSegCount = this.recv_queue[0].Frg + 1;
            for (int i = 0; i < packSegCount; i++)
            {
                Array.Copy(this.recv_queue[i].Data, 0, data, startIndex, this.recv_queue[i].Data.Length);
                startIndex += this.recv_queue[i].Data.Length;
            }
            this.recv_queue = slice<KCPPackage>(this.recv_queue, packSegCount, this.recv_queue.Length);
            return true;
        }

        //----------------------------help---------------------------------
        public static T[] slice<T>(T[] p, int start, int stop)
        {
            var arr = new T[stop - start];
            var index = 0;
            for (var i = start; i < stop; i++)
            {
                arr[index] = p[i];
                index++;
            }

            return arr;
        }
        public static T[] append<T>(T[] p, T c)
        {
            var arr = new T[p.Length + 1];
            for (var i = 0; i < p.Length; i++)
                arr[i] = p[i];
            arr[p.Length] = c;
            return arr;
        }
        public static T[] append<T>(T[] p, T[] cs)
        {
            var arr = new T[p.Length + cs.Length];
            for (var i = 0; i < p.Length; i++)
                arr[i] = p[i];
            for (var i = 0; i < cs.Length; i++)
                arr[p.Length + i] = cs[i];
            return arr;
        }
    }
    public class KCPPackage
    {
        public UInt32 Conv;
        public byte Cmd;
        public byte Frg;
        public UInt16 Wnd;
        public UInt32 Ts;
        public UInt32 Sn;
        public UInt32 Una;
        public UInt32 Length;
        public byte[] Data;
        static Tuple<KCPPackage, int> defaultval = new Tuple<KCPPackage, int>(null, 0);
        const int headlength = 24;


        //----非head的计数部分
        public int fastack = 0;
        public static Tuple<KCPPackage, int> Parse(byte[] data, int index)
        {
            if (data.Length - index < 24)
                return defaultval;
            var pack = new KCPPackage();
            pack.Conv =  ConverHelper.GetU32(data, index);
            pack.Cmd = ConverHelper.GetU8(data, index + 4);
            pack.Frg = ConverHelper.GetU8(data, index + 5);
            pack.Wnd = ConverHelper.GetU16(data, index + 6);
            pack.Ts = ConverHelper.GetU16(data, index + 8);
            pack.Sn = ConverHelper.GetU16(data, index + 12);
            pack.Una = ConverHelper.GetU16(data, index + 16);
            pack.Length = ConverHelper.GetU16(data, index + 20);
            if ((data.Length - index - headlength) < pack.Length)
                return defaultval;
            pack.Data = data.Skip(index + headlength).Take((int)pack.Length).ToArray();
            return new Tuple<KCPPackage, int>(pack, (int)pack.Length + headlength);
        }
    }
    public class ConverHelper
    {
        public static byte GetU8(byte[] data, int index)
        {
            return data[index];
        }
        public static ushort GetU16(byte[] data, int index)
        {
            //var temp = data[index];
            //data[index] = data[index + 1];
            //data[index + 1] = temp;
            return BitConverter.ToUInt16(data, index);
        }
        public static UInt32 GetU32(byte[] data, int index)
        {
            //var temp = data[index];
            //data[index] = data[index + 3];
            //data[index + 3] = temp;
            //temp = data[index + 1];
            //data[index + 1] = data[index + 2];
            //data[index + 2] = temp;
            return BitConverter.ToUInt32(data, index);
        }
    }
}
