using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            new Worker(this, this.Show).Start();
        }
        private void Show(Worker worker)
        {
            this.button1.Text = worker.state.ToString();
        }
    }
    public class Worker
    {
        System.Threading.Timer t = null;
        Form targetform = null;
        Action<Worker> callback = null;
        // -1 timeout 1 success
        public int state = 0;
        public Worker(Form form, Action<Worker> callback)
        {
            this.targetform = form;
            this.callback = callback;
        }
        public void Start(int waitCount = 5000)
        {
            t = new System.Threading.Timer(this.TimeCallback, null, waitCount, Timeout.Infinite);
            ThreadPool.QueueUserWorkItem(Work, null);
        }
        private void Work(object o)
        {
            try
            {
                Thread.Sleep(6000);
                if(this.state == 0)
                {
                    this.state = 1;
                    this.t.Dispose();
                    CallCallBack();
                }
            }
            catch
            {

            }
        }
        private void TimeCallback(object o)
        {
            if (this.state == 1)
                return;
            if(this.state == 0)
            {
                this.state = -1;
                this.t.Dispose();
                CallCallBack();
            }

        }
        private void CallCallBack()
        {
            try
            {
                if (this.callback != null && this.targetform != null)
                    this.targetform.Invoke((Delegate)this.callback, new object[] { this });
            }
            catch
            {

            }
        }
    }
}
