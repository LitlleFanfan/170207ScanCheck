using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO.Ports;
using System.IO;
using System.Windows.Forms;
using lgscan;

namespace BarCodeScan {
    public class PLC {
        //触点 X、Y、R
        //数据寄存器 D
        /*-----读取单触点状态[RCS] (XYR)------
         * 例:读取Y1
         * 发送:%01#RCSY0001**[CR]
         * 返回:%01$RC1**[CR]
         * 0="off",1="on"
         * ----------------------*/
        /*-----写入单触点状态[WCS] (XYR)------
         * 例:写入Y1="on"
         * 发送:%01#WCSY00011**[CR]
         * 返回:%01$WC**[CR]
         * ----------------------*/
        /*-----读取多触点状态[RCP] (XYR)------
         * 例:读取Y1-Y3
         * 发送:%01#RCP3Y0001R0001Y0003T0099**[CR]
         * 返回:%01$RC1011**[CR]
         * 0="off",1="on"
         * ----------------------*/
        /*-----读取Word型触点状态[RCC] (XYR)------
         * 例:读取Y1-Y3
         * 发送:%01#RCCY00010003**[CR]
         * 返回:%01$RC34127856BC9A**[CR]
         * ----------------------*/
        /*-----写入多触点状态[WCP] (XYR)------
         * 例:写入Y1-Y3为on
         * 发送:%01#WCP3Y00011R00021Y00031**[CR]
         * 返回:%01$WC**[CR]
         * 0="off",1="on"
         * ----------------------*/
        /*-----读取DT数据[RD]---------------
         *例:读取D1105-D1107的内容
         *发送:%01#RDD011050110757[CR]
         *返回:%01$RD630044330A0062[CR]
         *值:D1105=0063H,D1106=3344H,D1107=000AH
         *----------------------*/
        /*-----写入DT数据[WD]----------------
         * 例:写入数据到D1-D13 D1=0005H,D2=1507H,D=0900H
         * 发送:%01%WDD00001000030500071500095D[CR]
         * 返回:%01$WD13[CR]
         *----------------------*/
        private static TextBox txtCommLog = new TextBox();
        private static SerialPort comm = new SerialPort();
        // private static double[] DTValue = null;
        // private static System.Collections.BitArray[] arrXYMValue = null;
        // private const int iDelay = 60;
       private static bool bRecieveData;

        // 用于外部接收调试信息。
        private static Action<string> loghandler;
        private const int DELAY = 50;

        public static void setHandler(Action<string> handler) {
            loghandler = handler;
        }

        public static bool Open(string port, int baudrate) {
            try {
                if (Form1.conf == null) {
                    Form1.loadConf();
                }
                if (comm.IsOpen) {
                    comm.Close();
                }
                comm.PortName = port;
                comm.BaudRate = baudrate;
                comm.Parity = Parity.Odd;
                comm.DataBits = 8;
                comm.StopBits = StopBits.One;
                comm.NewLine = "\r\n";

                comm.ReadTimeout = 1000;
                comm.WriteTimeout = 1000;

                comm.Open();

                return true;
            } catch (Exception ex) {
                MessageBox.Show(ex.Message);
                return false;
            }
        }

        public static void Close() {
            if (comm.IsOpen) {
                comm.Close();
            }
        }

        private static void SetText(string text) //记录通信事件
        {
            loghandler?.Invoke(text);
        }

        public static string GetPLCData() {
            var readText = "";
            var Resoursedata = new byte[1024];
            var n = comm.Read(Resoursedata, 0, Resoursedata.Length);
            var b = Sub(Resoursedata, 0, n);
            readText = readText + ASCIIEncoding.ASCII.GetString(b);
            SetText("[PLC->PC]:" + readText.Trim());
            return readText;
        }

        /// <summary>
        /// 截取
        /// </summary>
        /// <param name="b1"></param>
        /// <param name="index"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static byte[] Sub(byte[] b1, int index, int length) {
            if (b1.Length < index + length + 1)
                return null;
            byte[] re = new byte[length];
            for (int i = 0; i < length; i++) {
                re[i] = b1[i + index];
            }
            return re;
        }
        /// <summary>
        /// 设置单触点
        /// setM(string XYMaddr,int value)
        /// XYMaddr:寄存器地址
        /// value=0 off;
        /// value=1 on;
        /// </summary>
        public static void setM(string XYMaddr, int value) //value 0=off,1=on
        {
            //发送:%01#WCSY00011**[CR]
            bRecieveData = false;
            string outStr = "";
            string sReg = XYMaddr.Substring(0, 1);
            string sAddr = XYMaddr.Substring(1, XYMaddr.Length - 1).PadLeft(4).Replace(" ", "0");
            outStr = "%01#WCS" + sReg + sAddr + value.ToString();
            outStr = outStr + bcc(outStr) + "\r";
            //outStr = outStr + "**" + "\r";
            comm.Write(outStr);
            Thread.Sleep(DELAY);
            SetText("[PC->PLC]:" + outStr);
            return;
        }

        public static void setMs(string[] XYMaddr) //value 0=off,1=on
        {
            //发送:%01#WCSY00011**[CR]
            bRecieveData = false;
            string outStr = "";
            string val = "";
            foreach (string s in XYMaddr) {
                string sReg = s.Substring(0, 1);
                string[] t = s.Split('/');
                string sAddr = t[0].Substring(1, t[0].Length - 1).Split('/')[0].PadLeft(4).Replace(" ", "0");
                val = val + sReg + sAddr + t[1];
            }
            outStr = "%01#WCP" + val;
            outStr = outStr + bcc(outStr) + "\r";
            //outStr = outStr + "**" + "\r";
            comm.Write(outStr);
            Thread.Sleep(DELAY);
            SetText("[PC->PLC]:" + outStr);
            return;
        }

        public static void read_RCS(string XYMaddr) {
            //发送:%01#RCSY0001**[CR]
            var outStr = "";
            var sReg = XYMaddr.Substring(0, 1);
            var sAddr = XYMaddr.Substring(1, XYMaddr.Length - 1).PadLeft(4).Replace(" ", "0");
            outStr = "%01#RCS" + sReg + sAddr;
            outStr = outStr + bcc(outStr) + "\r";
            //outStr = outStr + "**" + "\r";
            comm.Write(outStr);
            Thread.Sleep(DELAY);
            SetText("[PC->PLC]:" + outStr);
            return;
        }

        public static void read_RCP(string[] XYMaddr) {
            //发送:%01#RCSY0001**[CR]
            string outStr = "";
            string val = "";
            foreach (string s in XYMaddr) {
                string sReg = s.Substring(0, 1);
                string sAddr = s.Substring(1, s.Length - 1).PadLeft(4).Replace(" ", "0");
                val = val + sReg + sAddr;
            }
            outStr = "%01#RCP" + val;
            outStr = outStr + bcc(outStr) + "\r";
            //outStr = outStr + "**" + "\r";
            comm.Write(outStr);
            Thread.Sleep(DELAY);
            SetText("[PC->PLC]:" + outStr);
            return;
        }

        /// <summary>
        /// 本命令只是发送，取值，取值在OnDataReceived里
        ///按字读取触点
        /// readMS(string DTaddr1, string DTaddr2)
        /// DTaddr1:寄存器起始地址
        /// DTaddr2:寄存器结束地址
        /// 如:readMS("M1","M4");
        /// %01#RCCR00090009 读WR9到WR9
        /// </summary>
        /// <param name="chkString"></param>
        /// <returns></returns>
        public static void readMS(string DTaddr1, string DTaddr2) {            //-------------地址转换---------------------
            string sReg = DTaddr1.Substring(0, 1); //获取"D"
            string sA1 = DTaddr1.Substring(1, DTaddr1.Length - 1); //DT1中的1
            string sA2 = DTaddr2.Substring(1, DTaddr2.Length - 1); //DT4中的4
            int iLen = Convert.ToInt16(sA2) - Convert.ToInt16(sA1) + 1; //4-1 //---------------------按字读触点地址要除于10？
            sA1 = sA1.PadLeft(4).Replace(" ", "0");
            sA2 = sA2.PadLeft(4).Replace(" ", "0");
            string sAddr = sReg + sA1 + sA2; //地址:D0000100004
            //------------------------------------------
            string outStr = "";
            outStr = "%01#RCC" + sAddr;
            outStr = outStr + bcc(outStr) + "\r";
            comm.Write(outStr);
            Thread.Sleep(DELAY);
            SetText("[PC->PLC]:" + outStr);
            return;
        }
        /// <summary>
        /// writeDT(string DTaddr1,string DTaddr2, int value)
        /// DTaddr1:寄存器起始地址
        /// DTaddr2:寄存器结束地址
        /// value=数值;
        /// 如:writeDT("D1","D4",arrValue); 其中double[] arrValue={0.1,23.3,22.0,43.55};
        /// </summary>
        public static void writeDT(string DTaddr1, string DTaddr2, double[] value) {
            //发送:%01%WDD00001000030500071500095D[CR]
            //-------------地址转换---------------------
            string sReg = DTaddr1.Substring(0, 1); //获取"D"
            string sA1 = DTaddr1.Substring(1, DTaddr1.Length - 1); //DT1中的1
            string sA2 = DTaddr2.Substring(1, DTaddr2.Length - 1); //DT4中的4
            int iLen = Convert.ToInt16(sA2) - Convert.ToInt16(sA1) + 1; //4-1
            sA1 = sA1.PadLeft(5).Replace(" ", "0");
            sA2 = sA2.PadLeft(5).Replace(" ", "0");
            string sAddr = sReg + sA1 + sA2; //地址:D0000100004
            //-------------数值转换---------------------
            string sValues = "";
            string sValue = "";
            for (int i = 0; i < iLen; i++) {
                sValue = ((int)(value[i] * 1000)).ToString();
                sValue = Convert.ToInt32(sValue).ToString("X4");
                sValue = sValue.Substring(2, 2) + sValue.Substring(0, 2);
                sValues += sValue;
            }
            //------------------------------------------
            string outStr = "";
            outStr = "%01#WD" + sAddr + sValues;
            outStr = outStr + bcc(outStr) + "\r";
            comm.Write(outStr);
            Thread.Sleep(DELAY);
            SetText("[PC->PLC]:" + outStr);
            return;
        }
        /// <summary>
        /// 本命令只是发送，取值，取值在OnDataReceived里
        /// readDT(string DTaddr1, string DTaddr2)
        /// DTaddr1:寄存器起始地址
        /// DTaddr2:寄存器结束地址
        /// 如:readDT("D1","D4");
        /// </summary>
        /// <param name="chkString"></param>
        /// <returns></returns>
        public static void readDT(string DTaddr1, string DTaddr2) {            //-------------地址转换---------------------
            string sReg = DTaddr1.Substring(0, 1); //获取"D"
            string sA1 = DTaddr1.Substring(1, DTaddr1.Length - 1); //DT1中的1
            string sA2 = DTaddr2.Substring(1, DTaddr2.Length - 1); //DT4中的4
            int iLen = Convert.ToInt16(sA2) - Convert.ToInt16(sA1) + 1; //4-1
            sA1 = sA1.PadLeft(5).Replace(" ", "0");
            sA2 = sA2.PadLeft(5).Replace(" ", "0");
            string sAddr = sReg + sA1 + sA2; //地址:D0000100004
            //------------------------------------------
            string outStr = "";
            outStr = "%01#RD" + sAddr;
            outStr = outStr + bcc(outStr) + "\r";
            comm.Write(outStr);
            Thread.Sleep(DELAY);
            SetText("[PC->PLC]:" + outStr);
            return;
        }
        private static string bcc(string chkString) {
            int chkSum = 0;
            string chkSums = "";
            int k;
            for (k = 0; k < chkString.Length; k++) {
                chkSum = chkSum ^ Asc(chkString.Substring(k, 1));
            }
            chkSums = Convert.ToString(chkSum, 16);
            return chkSums.Substring(chkSums.Length - 2, 2).ToUpper();
        }
        private static int Asc(string character) {
            if (character.Length == 1) {
                System.Text.ASCIIEncoding asciiEncoding = new System.Text.ASCIIEncoding();
                int intAsciiCode = (int)asciiEncoding.GetBytes(character)[0];
                return (intAsciiCode);
            } else {
                throw new Exception("Character is not valid.");
            }
        }

        #region 写文件
        /// <summary>
        /// 写文件
        /// </summary>
        /// <param name="path"></param>
        public static void WriteFile(string sData) {
            try {

                string path = Application.StartupPath + "PLC" + "_" + DateTime.Now.ToString("yyyyMMdd") + ".log";
                if ("PLC" == "") {
                    path = @"C:\BarCodeScan_" + DateTime.Now.ToString("yyyyMMdd") + ".log";
                }
                FileStream fs = new FileStream(path, FileMode.Append);
                System.Text.Encoding code = System.Text.Encoding.GetEncoding("gb2312");
                StreamWriter sw = new StreamWriter(fs, code);
                //开始写入
                sw.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff") + "\t\t" + sData + "\r\n");
                //清空缓冲区
                sw.Flush();
                //关闭流
                sw.Close();
                fs.Close();

            } catch (Exception ex) {
                //MessageBox.Show("写日志文件出现了错：" + ex.Message);
            }
        }
        #endregion
    }
}
